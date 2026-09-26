namespace Deuna.Feedback.Service.DTOs;

// Contratos de "Calificaciones a restaurantes": el desempeño de los restaurantes
// según las calificaciones y recomendaciones que recibieron.
//
// Los nombres coinciden con los tipos que usa el portal
// (`src/lib/tipos/calificaciones-restaurantes.ts`): son el acuerdo entre las dos
// puntas, y un cambio acá obliga a un cambio allá. Todavía no hay endpoint ni
// consulta detrás; esto es solo el contrato.
//
// Reglas que valen para todo lo de abajo:
// - Nombres en español; fechas como ISO 8601 (`DateTime` sale así del serializador,
//   nunca como número).
// - Los conteos de la distribución son NÚMEROS, no porcentajes: el porcentaje lo
//   calcula la vista. Si el backend mandara el porcentaje, dos lugares harían la
//   misma cuenta y tarde o temprano diferirían.
// - Los aspectos son una lista de { Criterio, Promedio, Cantidad } y el criterio es
//   texto libre —es la decisión de TASK-402, criterios en filas para poder agregar
//   sin cambiar el esquema—, así que se devuelven los que existan, no un conjunto
//   fijo de cuatro.
// - Nulabilidad explícita en todo lo que puede faltar, con el motivo al lado.
// - Los agregados que no se pueden calcular viajan en `null`, no en `0`: un
//   promedio de cero y un promedio que no existe se ven igual en pantalla y
//   significan cosas opuestas.

/// <summary>
/// El sobre de la página.
///
/// <c>Total</c> es el total que cumple los filtros, no la cantidad de <c>Items</c>:
/// con <c>Tamano = 20</c>, la página 2 trae 20 items y un total de, por ejemplo, 137.
/// </summary>
public record PaginaCalificacionesRestaurantes(
    List<CalificacionDeRestaurante> Items,
    int Total,
    int Pagina,
    int Tamano);

/// <summary>
/// Una fila de la tabla: un restaurante con sus calificaciones ya agregadas.
///
/// El restaurante sale de la encuesta (`RestauranteId`) y su nombre y su zona de la
/// réplica de restaurantes que ya tiene Feedback: no hace falta pedirle datos a otro
/// servicio.
/// </summary>
public record CalificacionDeRestaurante(
    Guid RestauranteId,
    string Nombre,
    string Zona,
    // null hasta que exista el dato.
    string? TipoDeComida,
    // null si nunca lo calificaron.
    double? CalificacionPromedio,
    int TotalCalificaciones,
    // Conteos, no porcentajes: el porcentaje se calcula en la vista.
    DistribucionDeEstrellas Distribucion,
    // Nivel 2, agrupado por criterio.
    List<AspectoCalificado> Aspectos,
    TendenciaDelRestaurante Tendencia,
    // null mientras no exista el modelo de incidencias.
    int? Incidencias);

/// <summary>
/// Cuántas calificaciones recibió cada puntaje. Son conteos, no porcentajes: el
/// porcentaje lo calcula la vista, que es un solo lugar haciendo la cuenta.
/// </summary>
public record DistribucionDeEstrellas(
    int Cinco,
    int Cuatro,
    int Tres,
    int Dos,
    int Una);

/// <summary>
/// Un aspecto evaluado en el Nivel 2, ya agregado por restaurante.
/// </summary>
public record AspectoCalificado(
    // El nombre tal como llegó en la encuesta: texto libre, no un catálogo fijo.
    string Criterio,
    double Promedio,
    int Cantidad);

/// <summary>
/// El promedio de un aspecto sobre TODO el filtro, no sobre una página: es la misma forma que
/// <see cref="AspectoCalificado"/> —el criterio sigue siendo texto libre, así que se devuelven
/// los que existan y no un conjunto fijo de nombres—, con otro alcance.
///
/// Se agrega sobre el mismo conjunto filtrado que las filas, no con una consulta aparte: si se
/// calculara por su lado, un día el bloque de la pantalla diría un número y la tabla otro.
/// </summary>
public record AspectoDelResumen(
    string Criterio,
    double Promedio,
    int Cantidad);

/// <summary>Hacia dónde va el promedio del restaurante.</summary>
public record TendenciaDelRestaurante(
    // null sin base: "sin dato", no -100%.
    double? Variacion);

/// <summary>
/// Los KPIs de la cabecera.
///
/// Se calculan sobre el mismo conjunto que las filas, no con consultas separadas:
/// así el número de arriba y la lista de abajo no pueden discrepar.
/// </summary>
public record ResumenCalificacionesRestaurantes(
    // null si no hay ninguna calificación en el rango.
    double? PromedioGlobal,
    int RestaurantesCalificados,
    // Del total de restaurantes activos, no de los calificados.
    int RestaurantesTotales,
    int TotalCalificaciones,
    int CalificacionesHoy,
    // Los tres deltas del diseño, calculados sobre el mismo conjunto filtrado que las filas.
    VariacionesDelResumen Variaciones,
    DistribucionDeEstrellas Distribucion,
    // El promedio por aspecto de TODO el filtro, no de la página.
    List<AspectoDelResumen> Aspectos,
    // Para el gráfico.
    List<EvolucionDelPromedio> Evolucion,
    // ≥ 4.7.
    DestacadosDelResumen Destacados,
    // ≤ 3.5.
    EnAlertaDelResumen EnAlerta,
    List<RestauranteDelTop> Top);

/// <summary>
/// Los tres deltas del diseño: el promedio y el total contra el período anterior, y las
/// calificaciones de hoy contra las de ayer.
///
/// El período anterior es la MISMA ventana corrida hacia atrás su misma longitud: si el rango
/// es del 1 al 30, el anterior es del 1 al 30 del mes previo.
///
/// Los tres viajan en <c>null</c> cuando no hay base para comparar, y ahí está lo importante:
/// sin datos del período anterior un <c>0%</c> diría "no cambió nada" cuando en realidad no
/// sabemos. Nada de <c>0</c> ni de <c>-100%</c>.
/// </summary>
public record VariacionesDelResumen(
    // La diferencia del PROMEDIO, en puntos (0.3), no un porcentaje: el promedio va de 1 a 5
    // y "subió 0.3" es lo que se lee; un porcentaje sería otra cuenta sobre el mismo dato.
    double? PromedioVsPeriodoAnterior,
    // La variación PORCENTUAL del total de calificaciones (15.7).
    double? TotalVsPeriodoAnterior,
    // La variación PORCENTUAL de las calificaciones de hoy contra las de ayer (12.1).
    double? HoyVsAyer);

/// <summary>
/// El promedio de un día, que es un punto del gráfico de evolución.
/// </summary>
public record EvolucionDelPromedio(
    // ISO 8601.
    DateTime Fecha,
    double Promedio);

/// <summary>Los restaurantes destacados: promedio ≥ 4.7.</summary>
public record DestacadosDelResumen(
    double Minimo,
    int Cantidad);

/// <summary>Los restaurantes en alerta: promedio ≤ 3.5.</summary>
public record EnAlertaDelResumen(
    double Maximo,
    int Cantidad);

/// <summary>Una fila del top de restaurantes.</summary>
public record RestauranteDelTop(
    Guid RestauranteId,
    string Nombre,
    double Promedio,
    int Total);
