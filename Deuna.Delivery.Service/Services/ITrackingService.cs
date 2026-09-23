namespace Deuna.Delivery.Service.Services;

/// <summary>
/// Resuelve la ubicación del repartidor asociado a un pedido (US-003.3).
/// </summary>
public interface ITrackingService
{
    /// <summary>
    /// Devuelve la posición del repartidor del pedido. Si el pedido todavía no tiene
    /// repartidor asignado, busca el más cercano al punto de entrega. Null si no hay
    /// pedido o no hay telemetría disponible.
    /// </summary>
    Task<UbicacionGps?> ObtenerUbicacionDePedidoAsync(Guid pedidoId, CancellationToken cancellationToken = default);

    /// <summary>Registra la telemetría GPS de un repartidor.</summary>
    Task ActualizarUbicacionAsync(Guid riderId, double latitud, double longitud, DateTime timestamp, CancellationToken cancellationToken = default);
}
