using Microsoft.EntityFrameworkCore;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.DTOs;

namespace Deuna.Orders.Service.Services;

/// <summary>
/// Servicio para cálculo de tarifas de envío
/// </summary>
public interface ITarifaService
{
    /// <summary>
    /// Calcula la tarifa de envío basada en distancia, zonas de cobertura, etc.
    /// </summary>
    Task<TarifaCalculada> CalcularTarifaAsync(
        Guid restauranteId,
        double latEntrega,
        double lonEntrega,
        decimal subtotal,
        List<ItemPedido> items);
}

public record TarifaCalculada(
    string TipoTarifa,
    decimal CostoBase,
    decimal? CostoPorKm,
    decimal? DistanciaKm,
    decimal? CostoAdicional,
    string DetalleCalculo,
    decimal TotalCalculado
);

public class TarifaService : ITarifaService
{
    private readonly OrdersDbContext _db;
    private readonly IGeoService _geoService;

    public TarifaService(OrdersDbContext db, IGeoService geoService)
    {
        _db = db;
        _geoService = geoService;
    }

    public async Task<TarifaCalculada> CalcularTarifaAsync(
        Guid restauranteId,
        double latEntrega,
        double lonEntrega,
        decimal subtotal,
        List<ItemPedido> items)
    {
        var restaurante = await _db.RestaurantesReplicados
            .Include(r => r.ZonasCobertura.Where(z => z.Activo))
            .FirstOrDefaultAsync(r => r.Id == restauranteId);

        if (restaurante == null)
        {
            throw new InvalidOperationException($"Restaurante {restauranteId} no encontrado");
        }

        // Verificar cobertura por zonas poligonales primero
        foreach (var zona in restaurante.ZonasCobertura)
        {
            if (zona.Geom != null)
            {
                var enZona = await _geoService.PuntoEnPoligonoAsync(latEntrega, lonEntrega, zona.Geom);
                if (enZona)
                {
                    var costoTotal = zona.CostoEnvio;
                    if (subtotal < zona.PedidoMinimo)
                    {
                        costoTotal += zona.PedidoMinimo - subtotal; // Recargo por mínimo no alcanzado
                    }

                    return new TarifaCalculada(
                        TipoTarifa: "Zona",
                        CostoBase: zona.CostoEnvio,
                        CostoPorKm: null,
                        DistanciaKm: null,
                        CostoAdicional: subtotal < zona.PedidoMinimo ? zona.PedidoMinimo - subtotal : null,
                        DetalleCalculo: $"Zona: {zona.NombreZona}. Costo base: {zona.CostoEnvio:C}. {(subtotal < zona.PedidoMinimo ? $"Recargo por pedido mínimo ({zona.PedidoMinimo:C}): {zona.PedidoMinimo - subtotal:C}" : "Pedido mínimo alcanzado")}",
                        TotalCalculado: costoTotal
                    );
                }
            }
        }

        // Si no hay zona poligonal o no coincide, usar radio de cobertura circular
        var enRadio = await _geoService.EstaEnRadioCoberturaAsync(
            latEntrega, lonEntrega,
            (double)restaurante.Latitud, (double)restaurante.Longitud,
            (double)restaurante.RadioCoberturaKm);

        if (!enRadio)
        {
            var distancia = await _geoService.CalcularDistanciaMetrosAsync(
                latEntrega, lonEntrega,
                (double)restaurante.Latitud, (double)restaurante.Longitud);
            var distanciaKm = (decimal)(distancia / 1000);

            throw new InvalidOperationException(
                $"Dirección fuera de cobertura. Distancia: {distanciaKm:F2} km, Radio máximo: {restaurante.RadioCoberturaKm} km");
        }

        // Calcular distancia exacta para tarifa por distancia
        var distanciaMetros = await _geoService.CalcularDistanciaMetrosAsync(
            latEntrega, lonEntrega,
            (double)restaurante.Latitud, (double)restaurante.Longitud);
        var distanciaKmExacta = (decimal)(distanciaMetros / 1000);

        // Tarifa por distancia: costo base + costo por km * distancia
        // Valores por defecto si no hay configuración específica
        var costoBase = 3000m; // $3.000 COP base
        var costoPorKm = 1500m; // $1.500 COP por km

        // Buscar zona de cobertura tipo "radio" para configuración personalizada
        var zonaRadio = restaurante.ZonasCobertura
            .FirstOrDefault(z => z.NombreZona.ToLower().Contains("radio") || z.NombreZona.ToLower().Contains("default"));

        if (zonaRadio != null)
        {
            costoBase = zonaRadio.CostoEnvio;
        }

        var costoDistancia = costoPorKm * distanciaKmExacta;
        var totalCalculado = costoBase + costoDistancia;

        return new TarifaCalculada(
            TipoTarifa: "Distancia",
            CostoBase: costoBase,
            CostoPorKm: costoPorKm,
            DistanciaKm: Math.Round(distanciaKmExacta, 2),
            CostoAdicional: null,
            DetalleCalculo: $"Distancia: {distanciaKmExacta:F2} km. Costo base: {costoBase:C}. Costo por km: {costoPorKm:C} × {distanciaKmExacta:F2} km = {costoDistancia:C}",
            TotalCalculado: totalCalculado
        );
    }
}