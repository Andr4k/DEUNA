using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Services;

/// <summary>
/// Lectura de la pantalla de calificaciones a restaurantes (secciones 4.1 y 4.2 del plan).
///
/// El agregado por restaurante sale de un <c>GROUP BY</c> sobre <c>feedback_encuestas</c> del
/// rango; el nombre y la zona se resuelven contra la réplica de restaurantes que Feedback ya
/// tiene, sin pedirle datos a ningún otro servicio. Es una lectura de un solo servicio por
/// construcción: la encuesta ya guarda a qué restaurante califica.
///
/// La lista y el resumen comparten el mismo conjunto filtrado —lo arma
/// <see cref="ConstruirConjuntoAsync"/> una sola vez—, así que el KPI de la cabecera no puede
/// discrepar de la tabla. Los promedios se calculan como suma sobre conteo: el mismo par de
/// números que muestran la fila y el KPI, sin una segunda cuenta que se pueda desincronizar.
/// </summary>
public class CalificacionesRestaurantesService : ICalificacionesRestaurantesService
{
    /// <summary>Umbral de "destacado" (sección 1 del plan).</summary>
    private const double UmbralDestacado = 4.7;

    /// <summary>Umbral de "en alerta" (sección 1 del plan).</summary>
    private const double UmbralAlerta = 3.5;

    /// <summary>El top del resumen son cinco restaurantes.</summary>
    private const int TamanoDelTop = 5;

    private readonly FeedbackDbContext _db;
    private readonly ILogger<CalificacionesRestaurantesService> _logger;

    public CalificacionesRestaurantesService(
        FeedbackDbContext db,
        ILogger<CalificacionesRestaurantesService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PaginaCalificacionesRestaurantes> ObtenerCalificacionesAsync(
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken = default)
    {
        var conjunto = await ConstruirConjuntoAsync(filtro, cancellationToken);

        // El total es el del conjunto filtrado, no el de la página: con Tamano = 20, la página
        // 2 trae 20 items y un total de, por ejemplo, 137.
        var total = conjunto.Count;

        var pagina = Ordenar(conjunto)
            .Skip((filtro.Pagina - 1) * filtro.Tamano)
            .Take(filtro.Tamano)
            .ToList();

        var restauranteIds = pagina.Select(f => f.RestauranteId).ToList();

        // Los aspectos y la tendencia se piden solo para la página: son los dos cruces por
        // fila y no hacen falta para las que no se van a mostrar.
        var aspectos = await ObtenerAspectosAsync(restauranteIds, filtro, cancellationToken);
        var anteriores = await ObtenerPromediosAnterioresAsync(restauranteIds, filtro, cancellationToken);

        var items = pagina.Select(fila => new CalificacionDeRestaurante(
            RestauranteId: fila.RestauranteId,
            Nombre: fila.Nombre,
            Zona: fila.Zona,
            // No hay fuente de tipo de comida en ningún servicio: null, no una cadena vacía
            // que se leería como "sin tipo" en vez de "sin dato".
            TipoDeComida: null,
            CalificacionPromedio: fila.Promedio,
            TotalCalificaciones: fila.Total,
            Distribucion: fila.Distribucion,
            Aspectos: aspectos.TryGetValue(fila.RestauranteId, out var delRestaurante) ? delRestaurante : [],
            Tendencia: new TendenciaDelRestaurante(
                anteriores.TryGetValue(fila.RestauranteId, out var anterior)
                    ? Variacion(fila.Promedio, anterior)
                    : null),
            // Sin modelo de incidencias: el campo viaja para que la pantalla no cambie cuando
            // exista, y en null porque un 0 diría que no hubo ninguna.
            Incidencias: null)).ToList();

        return new PaginaCalificacionesRestaurantes(
            Items: items,
            Total: total,
            Pagina: filtro.Pagina,
            Tamano: filtro.Tamano);
    }

    public async Task<ResumenCalificacionesRestaurantes> ObtenerResumenAsync(
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken = default)
    {
        var conjunto = await ConstruirConjuntoAsync(filtro, cancellationToken);

        // El denominador del KPI "58 de 72" es el universo de restaurantes activos, no los
        // calificados: por eso no depende del rango ni de los filtros.
        var restaurantesTotales = await _db.RestaurantesReplicados
            .AsNoTracking()
            .CountAsync(r => r.Activo, cancellationToken);

        var totalCalificaciones = conjunto.Sum(f => f.Total);
        var sumaDePuntajes = conjunto.Sum(f => f.SumaDePuntajes);

        var restauranteIds = conjunto.Select(f => f.RestauranteId).ToList();

        return new ResumenCalificacionesRestaurantes(
            // El promedio global pondera cada calificación, no promedia los promedios de los
            // restaurantes: un restaurante con 40 encuestas pesa más que uno con una. Sin
            // ninguna calificación en el rango viaja en null, no en 0: un promedio de cero y
            // un promedio que no existe se ven igual en pantalla y significan cosas opuestas.
            PromedioGlobal: totalCalificaciones == 0
                ? null
                : (double)sumaDePuntajes / totalCalificaciones,
            RestaurantesCalificados: conjunto.Count,
            RestaurantesTotales: restaurantesTotales,
            TotalCalificaciones: totalCalificaciones,
            CalificacionesHoy: await ContarHoyAsync(restauranteIds, filtro, cancellationToken),
            Distribucion: SumarDistribuciones(conjunto),
            Evolucion: await EvolucionAsync(restauranteIds, filtro, cancellationToken),
            Destacados: new DestacadosDelResumen(
                Minimo: UmbralDestacado,
                Cantidad: conjunto.Count(f => f.Promedio >= UmbralDestacado)),
            EnAlerta: new EnAlertaDelResumen(
                Maximo: UmbralAlerta,
                Cantidad: conjunto.Count(f => f.Promedio <= UmbralAlerta)),
            Top: Ordenar(conjunto)
                .Take(TamanoDelTop)
                .Select(f => new RestauranteDelTop(
                    RestauranteId: f.RestauranteId,
                    Nombre: f.Nombre,
                    Promedio: f.Promedio,
                    Total: f.Total))
                .ToList());
    }

    /// <summary>
    /// El conjunto filtrado: una fila por restaurante con sus calificaciones del rango ya
    /// agregadas. Es el único lugar donde vive el WHERE de esta pantalla, y lo comparten la
    /// lista y el resumen.
    ///
    /// El <c>GROUP BY</c> y los conteos corren en la base; los filtros que son sobre el
    /// agregado —el nombre y la zona de la réplica, el rango del promedio— se aplican sobre
    /// esa fila ya agregada, que es una por restaurante.
    /// </summary>
    private async Task<List<FilaAgregada>> ConstruirConjuntoAsync(
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken)
    {
        var (desde, hasta, _) = Ventanas(filtro);

        var agregados = await _db.FeedbackEncuestas
            .AsNoTracking()
            .Where(e => e.CreatedAt >= desde && (hasta == null || e.CreatedAt < hasta))
            .GroupBy(e => e.RestauranteId)
            .Select(g => new
            {
                RestauranteId = g.Key,
                Total = g.Count(),
                SumaDePuntajes = g.Sum(e => e.RatingGeneralComida),
                Cinco = g.Count(e => e.RatingGeneralComida == 5),
                Cuatro = g.Count(e => e.RatingGeneralComida == 4),
                Tres = g.Count(e => e.RatingGeneralComida == 3),
                Dos = g.Count(e => e.RatingGeneralComida == 2),
                Una = g.Count(e => e.RatingGeneralComida == 1)
            })
            .ToListAsync(cancellationToken);

        if (agregados.Count == 0)
        {
            return [];
        }

        var restauranteIds = agregados.Select(a => a.RestauranteId).ToList();

        // Solo los activos: el denominador del KPI es el universo de activos, así que si el
        // numerador contara restaurantes dados de baja el KPI podría decir "5 de 3".
        var restaurantes = await _db.RestaurantesReplicados
            .AsNoTracking()
            .Where(r => r.Activo && restauranteIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        if (restaurantes.Count < agregados.Count)
        {
            // La réplica se alimenta del evento RestauranteRegistrado: que falte uno es una
            // pérdida de evento, y esas filas no se pueden nombrar ni ubicar. Se dejan fuera y
            // se avisa, en vez de mostrar una fila sin nombre.
            _logger.LogWarning(
                "Calificaciones: {SinReplica} de {Total} restaurantes con calificaciones no están activos en la réplica y quedan fuera de la pantalla",
                agregados.Count - restaurantes.Count, agregados.Count);
        }

        var filas = agregados
            .Where(a => restaurantes.ContainsKey(a.RestauranteId))
            .Select(a => new FilaAgregada(
                RestauranteId: a.RestauranteId,
                Nombre: restaurantes[a.RestauranteId].NombreComercial,
                Zona: restaurantes[a.RestauranteId].Ciudad,
                Total: a.Total,
                SumaDePuntajes: a.SumaDePuntajes,
                Promedio: (double)a.SumaDePuntajes / a.Total,
                Distribucion: new DistribucionDeEstrellas(
                    Cinco: a.Cinco,
                    Cuatro: a.Cuatro,
                    Tres: a.Tres,
                    Dos: a.Dos,
                    Una: a.Una)));

        if (!string.IsNullOrWhiteSpace(filtro.Zona))
        {
            // La zona de la fila es la ciudad del restaurante: se compara contra lo que muestra
            // la columna, sin distinguir mayúsculas.
            var zona = filtro.Zona.Trim().ToLowerInvariant();
            filas = filas.Where(f => f.Zona.ToLowerInvariant() == zona);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var patron = filtro.Buscar.Trim().ToLowerInvariant();
            filas = filas.Where(f => f.Nombre.ToLowerInvariant().Contains(patron));
        }

        if (filtro.CalificacionMin is { } minima)
        {
            // El rango es sobre el promedio del restaurante —el que muestra la fila—, no sobre
            // cada calificación.
            filas = filas.Where(f => f.Promedio >= minima);
        }

        if (filtro.CalificacionMax is { } maxima)
        {
            filas = filas.Where(f => f.Promedio <= maxima);
        }

        return filas.ToList();
    }

    /// <summary>
    /// Los aspectos del Nivel 2 de los restaurantes pedidos, agrupados por el criterio tal
    /// como llegó en la encuesta.
    ///
    /// El criterio es texto libre (TASK-402): "Sabor" y "sabor" son dos criterios distintos y
    /// se devuelven los que existan, no un conjunto canónico de cuatro.
    /// </summary>
    private async Task<Dictionary<Guid, List<AspectoCalificado>>> ObtenerAspectosAsync(
        List<Guid> restauranteIds,
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken)
    {
        if (restauranteIds.Count == 0)
        {
            return [];
        }

        var (desde, hasta, _) = Ventanas(filtro);

        var agrupados = await _db.FeedbackDetallesCriterios
            .AsNoTracking()
            .Join(
                _db.FeedbackEncuestas
                    .AsNoTracking()
                    .Where(e => e.CreatedAt >= desde
                                && (hasta == null || e.CreatedAt < hasta)
                                && restauranteIds.Contains(e.RestauranteId)),
                criterio => criterio.EncuestaId,
                encuesta => encuesta.Id,
                (criterio, encuesta) => new
                {
                    encuesta.RestauranteId,
                    criterio.Criterio,
                    criterio.Puntaje
                })
            .GroupBy(x => new { x.RestauranteId, x.Criterio })
            .Select(g => new
            {
                g.Key.RestauranteId,
                g.Key.Criterio,
                Suma = g.Sum(x => x.Puntaje),
                Cantidad = g.Count()
            })
            .ToListAsync(cancellationToken);

        return agrupados
            .GroupBy(a => a.RestauranteId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => grupo
                    .OrderBy(a => a.Criterio, StringComparer.Ordinal)
                    .Select(a => new AspectoCalificado(
                        Criterio: a.Criterio,
                        Promedio: (double)a.Suma / a.Cantidad,
                        Cantidad: a.Cantidad))
                    .ToList());
    }

    /// <summary>
    /// El promedio de cada restaurante en el período anterior, que es el mismo rango corrido
    /// una longitud hacia atrás.
    /// </summary>
    private async Task<Dictionary<Guid, double>> ObtenerPromediosAnterioresAsync(
        List<Guid> restauranteIds,
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken)
    {
        if (restauranteIds.Count == 0)
        {
            return [];
        }

        var (desde, _, desdeAnterior) = Ventanas(filtro);

        var anteriores = await _db.FeedbackEncuestas
            .AsNoTracking()
            .Where(e => e.CreatedAt >= desdeAnterior
                        && e.CreatedAt < desde
                        && restauranteIds.Contains(e.RestauranteId))
            .GroupBy(e => e.RestauranteId)
            .Select(g => new
            {
                RestauranteId = g.Key,
                Suma = g.Sum(e => e.RatingGeneralComida),
                Total = g.Count()
            })
            .ToListAsync(cancellationToken);

        return anteriores.ToDictionary(
            a => a.RestauranteId,
            a => (double)a.Suma / a.Total);
    }

    /// <summary>
    /// Las calificaciones de hoy, recortadas por la ventana: el KPI se calcula sobre el mismo
    /// conjunto que las filas, así que una calificación de hoy fuera del rango pedido no
    /// cuenta.
    /// </summary>
    private async Task<int> ContarHoyAsync(
        List<Guid> restauranteIds,
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken)
    {
        if (restauranteIds.Count == 0)
        {
            return 0;
        }

        var (desde, hasta, _) = Ventanas(filtro);

        var inicioDeHoy = DateTime.UtcNow.Date;
        var desdeHoy = inicioDeHoy > desde ? inicioDeHoy : desde;
        var hastaHoy = inicioDeHoy.AddDays(1);

        if (hasta is { } extremo && extremo < hastaHoy)
        {
            hastaHoy = extremo;
        }

        if (desdeHoy >= hastaHoy)
        {
            return 0;
        }

        return await _db.FeedbackEncuestas
            .AsNoTracking()
            .CountAsync(
                e => restauranteIds.Contains(e.RestauranteId)
                     && e.CreatedAt >= desdeHoy
                     && e.CreatedAt < hastaHoy,
                cancellationToken);
    }

    /// <summary>Un punto por día con el promedio de ese día, para el gráfico de evolución.</summary>
    private async Task<List<EvolucionDelPromedio>> EvolucionAsync(
        List<Guid> restauranteIds,
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken)
    {
        if (restauranteIds.Count == 0)
        {
            return [];
        }

        var (desde, hasta, _) = Ventanas(filtro);

        var porDia = await _db.FeedbackEncuestas
            .AsNoTracking()
            .Where(e => e.CreatedAt >= desde
                        && (hasta == null || e.CreatedAt < hasta)
                        && restauranteIds.Contains(e.RestauranteId))
            .GroupBy(e => e.CreatedAt.Date)
            .Select(g => new
            {
                Fecha = g.Key,
                Suma = g.Sum(e => e.RatingGeneralComida),
                Total = g.Count()
            })
            .ToListAsync(cancellationToken);

        return porDia
            .OrderBy(d => d.Fecha)
            .Select(d => new EvolucionDelPromedio(
                Fecha: d.Fecha,
                Promedio: (double)d.Suma / d.Total))
            .ToList();
    }

    /// <summary>
    /// La ventana del rango y la del período anterior.
    ///
    /// <c>Hasta</c> es exclusivo (sección 4.1). Sin él la ventana queda abierta hacia adelante
    /// y el período anterior se mide contra "ahora": sin un extremo no habría longitud que
    /// correr hacia atrás.
    /// </summary>
    private static (DateTime Desde, DateTime? Hasta, DateTime DesdeAnterior) Ventanas(
        FiltroCalificacionesRestaurantes filtro)
    {
        var hastaEfectivo = filtro.Hasta ?? DateTime.UtcNow;
        var duracion = hastaEfectivo - filtro.Desde;

        return (filtro.Desde, filtro.Hasta, filtro.Desde - duracion);
    }

    /// <summary>
    /// El orden de la pantalla, y también el del top del resumen: el promedio más alto
    /// primero. Los desempates van por total y después por nombre para que la paginación sea
    /// estable —sin un orden total, dos restaurantes con el mismo promedio podrían intercambiar
    /// lugar entre una página y la siguiente.
    /// </summary>
    private static IEnumerable<FilaAgregada> Ordenar(IEnumerable<FilaAgregada> filas)
        => filas
            .OrderByDescending(f => f.Promedio)
            .ThenByDescending(f => f.Total)
            .ThenBy(f => f.Nombre, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// La variación contra el período anterior, en porcentaje.
    ///
    /// Sin base no hay variación: null, no -100%. Una caída del 100% diría que el restaurante
    /// se derrumbó, y lo que pasa es que no hay con qué compararlo.
    /// </summary>
    private static double? Variacion(double actual, double anterior)
        => anterior == 0 ? null : (actual - anterior) / anterior * 100;

    private static DistribucionDeEstrellas SumarDistribuciones(IEnumerable<FilaAgregada> filas)
    {
        var cinco = 0;
        var cuatro = 0;
        var tres = 0;
        var dos = 0;
        var una = 0;

        foreach (var fila in filas)
        {
            cinco += fila.Distribucion.Cinco;
            cuatro += fila.Distribucion.Cuatro;
            tres += fila.Distribucion.Tres;
            dos += fila.Distribucion.Dos;
            una += fila.Distribucion.Una;
        }

        return new DistribucionDeEstrellas(cinco, cuatro, tres, dos, una);
    }

    /// <summary>
    /// Una fila del agregado: el restaurante con sus calificaciones del rango ya sumadas.
    ///
    /// Se guardan la suma y el total —no solo el promedio— para que el promedio global del
    /// resumen se pueda ponderar sin volver a la base ni promediar promedios.
    /// </summary>
    private record FilaAgregada(
        Guid RestauranteId,
        string Nombre,
        string Zona,
        int Total,
        int SumaDePuntajes,
        double Promedio,
        DistribucionDeEstrellas Distribucion);
}
