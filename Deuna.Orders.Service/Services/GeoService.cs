using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Deuna.Orders.Service.Models;

namespace Deuna.Orders.Service.Services;

/// <summary>
/// Servicio para cálculos geoespaciales usando PostGIS
/// </summary>
public interface IGeoService
{
    /// <summary>
    /// Calcula la distancia en metros entre dos puntos (WGS84)
    /// </summary>
    Task<double> CalcularDistanciaMetrosAsync(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Verifica si un punto está dentro de un polígono
    /// </summary>
    Task<bool> PuntoEnPoligonoAsync(double lat, double lon, Polygon poligono);

    /// <summary>
    /// Verifica si un punto está dentro del radio de cobertura (círculo)
    /// </summary>
    Task<bool> EstaEnRadioCoberturaAsync(double latPedido, double lonPedido, double latRestaurante, double lonRestaurante, double radioKm);
}

public class GeoService : IGeoService
{
    private readonly OrdersDbContext _db;

    public GeoService(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task<double> CalcularDistanciaMetrosAsync(double lat1, double lon1, double lat2, double lon2)
    {
        // Usar función SQL de PostGIS para cálculo preciso
        var sql = $@"SELECT geo.distance_meters({lat1}, {lon1}, {lat2}, {lon2})";
        var connection = _db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            return Convert.ToDouble(result);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async Task<bool> PuntoEnPoligonoAsync(double lat, double lon, Polygon poligono)
    {
        var sql = $@"SELECT geo.point_in_polygon({lat}, {lon}, ST_GeomFromText('{poligono.AsText()}', 4326))";
        var connection = _db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            return Convert.ToBoolean(result);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async Task<bool> EstaEnRadioCoberturaAsync(double latPedido, double lonPedido, double latRestaurante, double lonRestaurante, double radioKm)
    {
        var distanciaMetros = await CalcularDistanciaMetrosAsync(latPedido, lonPedido, latRestaurante, lonRestaurante);
        var radioMetros = radioKm * 1000;
        return distanciaMetros <= radioMetros;
    }
}