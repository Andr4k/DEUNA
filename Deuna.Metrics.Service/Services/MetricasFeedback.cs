using Deuna.Metrics.Service.DTOs;
using Deuna.Metrics.Service.Models;

namespace Deuna.Metrics.Service.Services;

public interface IMetricasFeedback
{
    Task<(double Promedio, int Encuestas)> CalificacionAsync(RangoTiempo rango, CancellationToken cancelacion);

    Task<IReadOnlyList<EventoActividad>> EventosAsync(RangoTiempo rango, CancellationToken cancelacion);
}

/// <summary>
/// Subservicio de feedback. Lee **solo** `deuna_feedback`.
///
/// La calificación promedio sale de `RatingGeneralComida`, que es la nota que el
/// cliente le puso a la comida — no el promedio de todos los criterios del Nivel 2.
/// Promediar criterios distintos daría un número que no significa nada.
/// </summary>
public sealed class MetricasFeedback(
    FuenteDatos datosFeedback,
    Microsoft.Extensions.Options.IOptions<MetricasOptions> opciones) : IMetricasFeedback
{
    private readonly MetricasOptions _opciones = opciones.Value;

    public async Task<(double Promedio, int Encuestas)> CalificacionAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        const string sql = """
            select
                coalesce(avg("RatingGeneralComida"), 0) as Promedio,
                count(*)                                as Encuestas
            from feedback_encuestas
            where "CreatedAt" >= @Desde and "CreatedAt" < @Hasta
            """;

        var fila = await datosFeedback.ConsultarUnaAsync<FilaCalificacion>(sql, rango, cancelacion);

        return (fila?.Promedio ?? 0, fila?.Encuestas ?? 0);
    }

    public async Task<IReadOnlyList<EventoActividad>> EventosAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        const string sql = """
            select
                e."CreatedAt"                                                   as Fecha,
                'servicio'                                                       as Tipo,
                'Un cliente calificó con ' || e."RatingGeneralComida" ||
                ' de 5' || case when e."DeseaRecomendar" then ' y recomienda el restaurante' else '' end
                                                                                 as Texto
            from feedback_encuestas e
            where e."CreatedAt" >= @Desde and e."CreatedAt" < @Hasta
            order by e."CreatedAt" desc
            limit @Maximo
            """;

        return await datosFeedback.ConsultarAsync<EventoActividad>(
            sql,
            new { rango.Desde, rango.Hasta, Maximo = _opciones.MaximoEventosActividad },
            cancelacion);
    }

    private sealed class FilaCalificacion
    {
        public double Promedio { get; set; }
        public int Encuestas { get; set; }
    }
}
