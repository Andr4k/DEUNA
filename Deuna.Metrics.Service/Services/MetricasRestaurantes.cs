using Deuna.Metrics.Service.DTOs;

namespace Deuna.Metrics.Service.Services;

public interface IMetricasRestaurantes
{
    Task<ResumenRestaurantes> ResumenAsync(RangoTiempo rango, CancellationToken cancelacion);
}

/// <summary>
/// Subservicio de restaurantes. Lee **solo** `deuna_identity`.
///
/// Devuelve `ConPedidosHoy` en cero y no es un olvido: cuántos restaurantes
/// recibieron pedidos hoy vive en la base de Orders. Ese dato lo aporta el
/// subservicio de pedidos y lo une quien compone; acá no se cruza la base.
/// </summary>
public sealed class MetricasRestaurantes(FuenteDatos datosRestaurantes) : IMetricasRestaurantes
{
    public async Task<ResumenRestaurantes> ResumenAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        const string sql = """
            select
                count(*) filter (where "Activo")::int                    as Activos,
                count(*) filter (where "CreatedAt" >= @Desde
                                   and "CreatedAt" < @Hasta)::int        as NuevosHoy
            from perfiles_restaurante
            """;

        var fila = await datosRestaurantes.ConsultarUnaAsync<FilaRestaurantes>(sql, rango, cancelacion);

        return new ResumenRestaurantes(fila?.Activos ?? 0, 0, fila?.NuevosHoy ?? 0);
    }

    private sealed class FilaRestaurantes
    {
        public int Activos { get; set; }
        public int NuevosHoy { get; set; }
    }
}
