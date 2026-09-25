using Deuna.Metrics.Service.Services;

namespace Deuna.Metrics.Service.Endpoints;

/// <summary>
/// Endpoints del servicio de métricas.
///
/// Todos exigen rol ADMIN. El domiciliario y el restaurante tienen su propio panel
/// (con sus propios números) y no deberían poder pedir el agregado de toda la red:
/// el rol se exige acá, en el servidor, no escondiendo una pantalla en el portal.
/// </summary>
public static class MetricasEndpoints
{
    public static IEndpointRouteBuilder MapMetricasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/metrics")
            .WithTags("Metrics")
            .RequireAuthorization("admin");

        grupo.MapGet("/panel", async (
                IMetricasPanel metricas,
                CancellationToken cancelacion) =>
                Results.Ok(await metricas.PanelAsync(cancelacion)))
            .WithName("MetricasPanel")
            .WithSummary("Todos los indicadores del panel en una sola llamada");

        grupo.MapGet("/zonas", async (
                IMetricasPanel metricas,
                CancellationToken cancelacion) =>
                Results.Ok(await metricas.ZonasAsync(cancelacion)))
            .WithName("MetricasZonas")
            .WithSummary("Actividad y recaudo por zona");

        grupo.MapGet("/rendimiento", async (
                IMetricasPanel metricas,
                CancellationToken cancelacion) =>
                Results.Ok(await metricas.RendimientoAsync(cancelacion)))
            .WithName("MetricasRendimiento")
            .WithSummary("Entregas a tiempo, tiempo promedio y calificación");

        grupo.MapGet("/actividad", async (
                IMetricasPanel metricas,
                CancellationToken cancelacion) =>
                Results.Ok(await metricas.ActividadAsync(cancelacion)))
            .WithName("MetricasActividad")
            .WithSummary("Últimos eventos de la red, de todos los dominios");

        return app;
    }
}
