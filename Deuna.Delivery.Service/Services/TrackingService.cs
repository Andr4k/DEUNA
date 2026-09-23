using Deuna.Delivery.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Delivery.Service.Services;

/// <summary>
/// Orquesta la consulta de tracking: resuelve el pedido en el read-model local y
/// obtiene la posición desde Redis.
/// </summary>
public class TrackingService : ITrackingService
{
    /// <summary>
    /// Radio de búsqueda del repartidor más cercano al punto de entrega.
    /// El plan técnico (02-technical-plan.md) define 5 km; el grafo de tareas menciona 10 km.
    /// </summary>
    public const double RadioBusquedaKm = 5;

    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly ILogger<TrackingService> _logger;

    public TrackingService(DeliveryDbContext db, ITrackingStore store, ILogger<TrackingService> logger)
    {
        _db = db;
        _store = store;
        _logger = logger;
    }

    public Task ActualizarUbicacionAsync(Guid riderId, double latitud, double longitud, DateTime timestamp, CancellationToken cancellationToken = default)
        => _store.ActualizarAsync(riderId, latitud, longitud, timestamp, cancellationToken);

    public async Task<UbicacionGps?> ObtenerUbicacionDePedidoAsync(Guid pedidoId, CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning("Tracking solicitado para un pedido inexistente: {PedidoId}", pedidoId);
            return null;
        }

        // Caso normal: el pedido ya tiene repartidor; se devuelve su posición exacta.
        if (pedido.RepartidorId is { } repartidorId)
        {
            return await _store.ObtenerAsync(repartidorId, cancellationToken);
        }

        // Pedido sin asignar: se busca el repartidor más cercano al punto de entrega.
        if (pedido.Latitud is { } latitud && pedido.Longitud is { } longitud)
        {
            return await _store.BuscarCercanoAsync(latitud, longitud, RadioBusquedaKm, cancellationToken);
        }

        _logger.LogWarning(
            "El pedido {PedidoId} no tiene repartidor ni punto de entrega proyectado", pedidoId);
        return null;
    }
}
