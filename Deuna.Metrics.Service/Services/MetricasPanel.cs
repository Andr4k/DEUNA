using Deuna.Metrics.Service.DTOs;
using Deuna.Metrics.Service.Models;
using Microsoft.Extensions.Options;

namespace Deuna.Metrics.Service.Services;

public interface IMetricasPanel
{
    Task<ResumenPanel> PanelAsync(CancellationToken cancelacion);
    Task<IReadOnlyList<Zona>> ZonasAsync(CancellationToken cancelacion);
    Task<RendimientoDelDia> RendimientoAsync(CancellationToken cancelacion);
    Task<IReadOnlyList<Actividad>> ActividadAsync(CancellationToken cancelacion);
}

/// <summary>
/// Compone las respuestas del panel a partir de los subservicios de dominio.
///
/// Acá —y solo acá— se unen datos de dominios distintos, y se unen **en memoria**,
/// nunca con una consulta entre bases. Es la diferencia entre que un restaurante
/// nuevo aparezca en el panel y que el servicio de métricas tenga que conocer el
/// esquema interno de los otros servicios para siempre.
///
/// Las consultas independientes corren en paralelo: son cuatro bases distintas, y
/// encadenarlas sumaría cuatro latencias sin ninguna razón.
/// </summary>
public sealed class MetricasPanel(
    IMetricasPedidos pedidos,
    IMetricasDomiciliarios domiciliarios,
    IMetricasFeedback feedback,
    IMetricasRestaurantes restaurantes,
    RelojDelPanel reloj,
    IOptions<MetricasOptions> opciones) : IMetricasPanel
{
    private readonly MetricasOptions _opciones = opciones.Value;

    public async Task<ResumenPanel> PanelAsync(CancellationToken cancelacion)
    {
        var hoy = reloj.Hoy();
        var ayer = reloj.Ayer();

        var indicadoresTarea = pedidos.IndicadoresAsync(hoy, ayer, cancelacion);
        var porEstadoTarea = pedidos.PorEstadoAsync(hoy, cancelacion);
        var conPedidosTarea = pedidos.RestaurantesConPedidosAsync(hoy, cancelacion);
        var flotaTarea = domiciliarios.ResumenAsync(cancelacion);
        var restaurantesTarea = restaurantes.ResumenAsync(hoy, cancelacion);

        await Task.WhenAll(
            indicadoresTarea, porEstadoTarea, conPedidosTarea, flotaTarea, restaurantesTarea);

        var (hoyDatos, ayerDatos) = await indicadoresTarea;
        var red = await restaurantesTarea;

        return new ResumenPanel(
            Indicadores: hoyDatos with { Variacion = Variacion(hoyDatos, ayerDatos) },
            PedidosPorEstado: await porEstadoTarea,
            Domiciliarios: await flotaTarea,
            // `ConPedidosHoy` viene de pedidos, no de Identity: acá se unen los dos
            // dominios en memoria.
            Restaurantes: red with { ConPedidosHoy = await conPedidosTarea });
    }

    public Task<IReadOnlyList<Zona>> ZonasAsync(CancellationToken cancelacion) =>
        pedidos.ZonasAsync(reloj.Hoy(), cancelacion);

    public async Task<RendimientoDelDia> RendimientoAsync(CancellationToken cancelacion)
    {
        var hoy = reloj.Hoy();

        var tiemposTarea = domiciliarios.TiemposEntregaAsync(hoy, cancelacion);
        var calificacionTarea = feedback.CalificacionAsync(hoy, cancelacion);

        await Task.WhenAll(tiemposTarea, calificacionTarea);

        var (aTiempo, medidas, promedio) = await tiemposTarea;
        var (calificacion, encuestas) = await calificacionTarea;

        return new RendimientoDelDia(
            aTiempo,
            promedio,
            // Sin encuestas la calificación es 0, no "sin dato": el portal dibuja la
            // estrella igual y un `null` la dejaría en blanco sin explicación.
            Math.Round(calificacion, 1),
            // Se informa cuántas entregas se midieron: un 95% sobre 2 entregas no
            // significa lo mismo que sobre 200, y el panel puede decirlo.
            EntregasMedidas: medidas);
    }

    /// <summary>
    /// Feed de actividad: los eventos de los tres dominios, ordenados por fecha.
    ///
    /// Cada subservicio devuelve los suyos y acá se recortan. Si cada uno devolviera
    /// ya recortado, el feed final mostraría solo los últimos de cada dominio en
    /// lugar de los últimos de la red.
    /// </summary>
    public async Task<IReadOnlyList<Actividad>> ActividadAsync(CancellationToken cancelacion)
    {
        var hoy = reloj.Hoy();

        var tareas = new[]
        {
            pedidos.EventosAsync(hoy, cancelacion),
            domiciliarios.EventosAsync(hoy, cancelacion),
            feedback.EventosAsync(hoy, cancelacion),
        };

        var todos = (await Task.WhenAll(tareas)).SelectMany(eventos => eventos);

        return todos
            .OrderByDescending(evento => evento.Fecha)
            .Take(_opciones.MaximoEventosActividad)
            .Select(evento => new Actividad(
                Hora: evento.Fecha.ToLocalTime().ToString("HH:mm"),
                Tipo: evento.Tipo,
                Texto: evento.Texto))
            .ToList();
    }

    private static VariacionDelDia Variacion(IndicadoresDelDia hoy, IndicadoresDelDia ayer) =>
        new(
            hoy.PedidosTotales - ayer.PedidosTotales,
            hoy.PedidosEnCurso - ayer.PedidosEnCurso,
            hoy.PedidosEntregados - ayer.PedidosEntregados,
            hoy.Incidencias - ayer.Incidencias,
            hoy.Recaudo - ayer.Recaudo);
}
