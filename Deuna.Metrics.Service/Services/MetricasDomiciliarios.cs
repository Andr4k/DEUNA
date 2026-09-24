using Deuna.Metrics.Service.DTOs;
using Deuna.Metrics.Service.Models;
using Microsoft.Extensions.Options;

namespace Deuna.Metrics.Service.Services;

public interface IMetricasDomiciliarios
{
    Task<ResumenDomiciliarios> ResumenAsync(CancellationToken cancelacion);

    Task<(int ATiempo, int Medidas, int PromedioMin)> TiemposEntregaAsync(
        RangoTiempo rango, CancellationToken cancelacion);

    Task<IReadOnlyList<EventoActividad>> EventosAsync(RangoTiempo rango, CancellationToken cancelacion);
}

/// <summary>
/// Subservicio de domiciliarios. Lee **solo** `deuna_delivery`.
///
/// La disponibilidad real de la flota (quién está conectado y dónde) vive en Redis,
/// no en esta base: acá se deriva del estado de las asignaciones. Es una foto
/// razonable para el panel, y está dicho para que nadie la confunda con la
/// telemetría en vivo.
/// </summary>
public sealed class MetricasDomiciliarios(
    FuenteDatos datosDomiciliarios,
    IOptions<MetricasOptions> opciones) : IMetricasDomiciliarios
{
    private readonly MetricasOptions _opciones = opciones.Value;

    /// <summary>
    /// Estado de la flota derivado de las asignaciones:
    ///
    /// - **En servicio**: tiene una asignación abierta (ni entregada ni rechazada).
    /// - **Disponibles**: está activo y no tiene ninguna asignación abierta.
    /// - **En descanso**: la réplica lo marca inactivo.
    /// </summary>
    public async Task<ResumenDomiciliarios> ResumenAsync(CancellationToken cancelacion)
    {
        const string sql = """
            with abiertas as (
                select distinct "RepartidorId"
                from asignaciones_repartidor
                where "FechaEntrega" is null
                  and "Estado" not in ('Completado', 'Rechazado')
            )
            select
                count(*) filter (where r."Activo")::int                                   as Activos,
                count(*) filter (where r."Activo" and a."RepartidorId" is null)::int      as Disponibles,
                count(*) filter (where r."Activo" and a."RepartidorId" is not null)::int  as EnServicio,
                count(*) filter (where not r."Activo")::int                               as EnDescanso
            from repartidores_replicados r
            left join abiertas a on a."RepartidorId" = r."Id"
            """;

        return await datosDomiciliarios.ConsultarUnaAsync<ResumenDomiciliarios>(sql, null, cancelacion)
            ?? new ResumenDomiciliarios(0, 0, 0, 0);
    }

    /// <summary>
    /// Tiempos de entrega del día.
    ///
    /// Se mide desde la asignación hasta la entrega: es el tiempo que el negocio
    /// promete al cliente, no solo el tramo final en moto.
    ///
    /// `ATiempo` hoy compara contra un umbral fijo en configuración. El pedido
    /// todavía no guarda un tiempo prometido (el radio y el horario del restaurante
    /// están fijos en el código de Orders), así que no hay contra qué comparar de
    /// verdad. Cuando exista, este umbral desaparece.
    /// </summary>
    public async Task<(int ATiempo, int Medidas, int PromedioMin)> TiemposEntregaAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        const string sql = """
            select
                count(*) filter (
                    where extract(epoch from ("FechaEntrega" - "FechaAsignacion")) / 60
                          <= @UmbralMinutos
                )                                                       as ATiempo,
                count(*)                                                as Medidas,
                coalesce(avg(extract(epoch from ("FechaEntrega" - "FechaAsignacion")) / 60), 0) as PromedioMin
            from asignaciones_repartidor
            where "FechaEntrega" is not null
              and "FechaAsignacion" is not null
              and "FechaEntrega" >= @Desde and "FechaEntrega" < @Hasta
            """;

        var fila = await datosDomiciliarios.ConsultarUnaAsync<FilaTiempos>(
            sql,
            new { rango.Desde, rango.Hasta, UmbralMinutos = _opciones.MinutosParaConsiderarATiempo },
            cancelacion) ?? new FilaTiempos();

        return (fila.ATiempo, fila.Medidas, (int)Math.Round(fila.PromedioMin));
    }

    public async Task<IReadOnlyList<EventoActividad>> EventosAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        // El código del pedido sale de `pedidos_disponibles`, que es la réplica que
        // este servicio ya tiene. NO se une `direcciones_entrega`: esa tabla vive en
        // `deuna_orders`, y una consulta entre bases es justo lo que este servicio no
        // puede hacer. Si se necesita la ciudad acá, se réplica por evento — no se
        // cruza la base.
        const string sql = """
            select
                a."FechaEntrega"                                              as Fecha,
                'entrega'                                                     as Tipo,
                coalesce(r."NombreCompleto", 'Un domiciliario') ||
                ' entregó el pedido ' || coalesce(p."Codigo", 'pendiente')     as Texto
            from asignaciones_repartidor a
            left join repartidores_replicados r on r."Id" = a."RepartidorId"
            left join pedidos_disponibles p on p."PedidoId" = a."PedidoId"
            where a."FechaEntrega" >= @Desde and a."FechaEntrega" < @Hasta
            order by a."FechaEntrega" desc
            limit @Maximo
            """;

        return await datosDomiciliarios.ConsultarAsync<EventoActividad>(
            sql,
            new { rango.Desde, rango.Hasta, Maximo = _opciones.MaximoEventosActividad },
            cancelacion);
    }

    private sealed class FilaTiempos
    {
        public int ATiempo { get; set; }
        public int Medidas { get; set; }
        public double PromedioMin { get; set; }
    }
}
