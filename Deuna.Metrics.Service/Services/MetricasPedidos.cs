using Deuna.Metrics.Service.DTOs;
using Deuna.Metrics.Service.Models;
using Deuna.Shared.Domain;
using Microsoft.Extensions.Options;

namespace Deuna.Metrics.Service.Services;

public interface IMetricasPedidos
{
    Task<(IndicadoresDelDia Hoy, IndicadoresDelDia Ayer)> IndicadoresAsync(
        RangoTiempo hoy, RangoTiempo ayer, CancellationToken cancelacion);

    Task<IReadOnlyList<ConteoPorEstado>> PorEstadoAsync(RangoTiempo rango, CancellationToken cancelacion);

    Task<IReadOnlyList<Zona>> ZonasAsync(RangoTiempo rango, CancellationToken cancelacion);

    Task<int> RestaurantesConPedidosAsync(RangoTiempo rango, CancellationToken cancelacion);

    Task<IReadOnlyList<EventoActividad>> EventosAsync(RangoTiempo rango, CancellationToken cancelacion);
}

/// <summary>
/// Subservicio de pedidos. Lee **solo** `deuna_orders`.
///
/// El recaudo sale de `tarifas_aplicadas.TotalCalculado`, que es la tarifa que el
/// backend calculó y guardó al crear el pedido — no una constante copiada acá. Si
/// mañana cambia el modelo de comisión, este número sigue siendo correcto sin tocar
/// el servicio.
/// </summary>
public sealed class MetricasPedidos(FuenteDatos datosPedidos, IOptions<MetricasOptions> opciones)
    : IMetricasPedidos
{
    private readonly MetricasOptions _opciones = opciones.Value;

    /// <summary>
    /// Estados en los que el pedido sigue vivo, tomados del contrato compartido.
    ///
    /// No se escriben los literales acá: `Deuna.Shared.Domain.EstadosPedido` es la
    /// fuente única, y repetir `'Buscando'` a mano es exactamente cómo un servicio
    /// se queda mirando un estado que ya no existe sin que nada falle.
    /// </summary>
    private static readonly string[] EnProgreso =
    [
        EstadosPedido.Buscando,
        EstadosPedido.Asignado,
        EstadosPedido.ConfirmadoEnLocal,
        EstadosPedido.EnRuta,
    ];

    public async Task<(IndicadoresDelDia Hoy, IndicadoresDelDia Ayer)> IndicadoresAsync(
        RangoTiempo hoy,
        RangoTiempo ayer,
        CancellationToken cancelacion)
    {
        const string sql = """
            select
                count(*) filter (where p."Estado" = any(@EnProgreso))             as PedidosEnCurso,
                count(*) filter (where p."Estado" = @Entregado)                    as PedidosEntregados,
                count(*) filter (where p."Estado" = @Cancelado)                    as Incidencias,
                coalesce(sum(t."TotalCalculado"), 0)                               as Recaudo,
                count(*)                                                           as PedidosTotales
            from pedidos p
            left join tarifas_aplicadas t on t."PedidoId" = p."Id"
            where p."FechaCreacion" >= @Desde and p."FechaCreacion" < @Hasta
            """;

        var hoyDatos = await datosPedidos.ConsultarUnaAsync<FilaIndicadores>(sql, Parametros(hoy), cancelacion)
            ?? new FilaIndicadores();
        var ayerDatos = await datosPedidos.ConsultarUnaAsync<FilaIndicadores>(sql, Parametros(ayer), cancelacion)
            ?? new FilaIndicadores();

        return (Convertir(hoyDatos), Convertir(ayerDatos));
    }

    /// <summary>Parámetros comunes: el rango más los estados del contrato.</summary>
    private static object Parametros(RangoTiempo rango) => new
    {
        rango.Desde,
        rango.Hasta,
        EnProgreso,
        Entregado = EstadosPedido.Entregado,
        Cancelado = EstadosPedido.Cancelado,
    };

    public async Task<IReadOnlyList<ConteoPorEstado>> PorEstadoAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        const string sql = """
            select "Estado" as Estado, count(*) as Valor
            from pedidos
            where "FechaCreacion" >= @Desde and "FechaCreacion" < @Hasta
            group by "Estado"
            order by count(*) desc
            """;

        return await datosPedidos.ConsultarAsync<ConteoPorEstado>(sql, rango, cancelacion);
    }

    /// <summary>
    /// Pedidos por zona.
    ///
    /// La zona es la ciudad de la dirección de entrega. El `translate` no es
    /// decorativo: en la base conviven `Bogotá` y `Bogota`, y sin normalizar la
    /// misma ciudad aparece como dos zonas distintas, cada una con la mitad de los
    /// pedidos. Se resuelve acá en lugar de pedirle a alguien que limpie los datos:
    /// la métrica no puede depender de que la escritura sea prolija.
    /// </summary>
    public async Task<IReadOnlyList<Zona>> ZonasAsync(RangoTiempo rango, CancellationToken cancelacion)
    {
        const string sql = """
            select
                initcap(trim(translate(lower(coalesce(nullif(trim(d."Ciudad"), ''), 'Sin ciudad')),
                                        'áéíóúüñàèìòùâêîôû', 'aeiouunaeiouaeiou'))) as Nombre,
                count(*)                                                              as Pedidos,
                count(*) filter (where p."Estado" = @Entregado)                       as Entregados,
                count(*) filter (where p."Estado" = any(@EnProgreso))                 as Pendientes,
                coalesce(sum(t."TotalCalculado"), 0)                                  as Recaudo
            from pedidos p
            left join direcciones_entrega d on d."Id" = p."DireccionEntregaId"
            left join tarifas_aplicadas t on t."PedidoId" = p."Id"
            where p."FechaCreacion" >= @Desde and p."FechaCreacion" < @Hasta
            group by 1
            order by count(*) desc
            limit @Maximo
            """;

        return await datosPedidos.ConsultarAsync<Zona>(
            sql,
            new
            {
                rango.Desde,
                rango.Hasta,
                EnProgreso,
                Entregado = EstadosPedido.Entregado,
                Maximo = _opciones.ZonasMaximas,
            },
            cancelacion);
    }

    public async Task<int> RestaurantesConPedidosAsync(RangoTiempo rango, CancellationToken cancelacion)
    {
        const string sql = """
            select count(distinct "RestauranteId")
            from pedidos
            where "FechaCreacion" >= @Desde and "FechaCreacion" < @Hasta
            """;

        return await datosPedidos.ConsultarUnaAsync<int>(sql, rango, cancelacion);
    }

    public async Task<IReadOnlyList<EventoActividad>> EventosAsync(
        RangoTiempo rango,
        CancellationToken cancelacion)
    {
        // Solo el día en curso: el feed es de "qué está pasando ahora", no un
        // histórico.
        const string sql = """
            select
                p."FechaCreacion"                                            as Fecha,
                'pedido'                                                     as Tipo,
                'Pedido ' || p."Codigo" || ' desde ' ||
                coalesce(r."NombreComercial", 'un restaurante')              as Texto,
                p."Estado"                                                   as Estado
            from pedidos p
            left join restaurantes_replicados r on r."Id" = p."RestauranteId"
            where p."FechaCreacion" >= @Desde and p."FechaCreacion" < @Hasta
            order by p."FechaCreacion" desc
            limit @Maximo
            """;

        var filas = await datosPedidos.ConsultarAsync<FilaEventoPedido>(
            sql,
            new { rango.Desde, rango.Hasta, Maximo = _opciones.MaximoEventosActividad },
            cancelacion);

        return filas
            .Select(f => new EventoActividad(f.Fecha, f.Tipo, $"{f.Texto} ({f.Estado})"))
            .ToList();
    }

    private static IndicadoresDelDia Convertir(FilaIndicadores f) =>
        new(f.PedidosTotales, f.PedidosEnCurso, f.PedidosEntregados, f.Incidencias, f.Recaudo,
            new VariacionDelDia(0, 0, 0, 0, 0));

    private sealed class FilaIndicadores
    {
        public int PedidosTotales { get; set; }
        public int PedidosEnCurso { get; set; }
        public int PedidosEntregados { get; set; }
        public int Incidencias { get; set; }
        public decimal Recaudo { get; set; }
    }

    private sealed class FilaEventoPedido
    {
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; } = "pedido";
        public string Texto { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
    }
}
