namespace Deuna.Delivery.Service.Services;

/// <summary>Posición GPS de un repartidor, con distancia opcional al punto consultado.</summary>
public record UbicacionGps(
    Guid RiderId,
    double Latitud,
    double Longitud,
    double? DistanciaMetros,
    DateTime? ActualizadoEn
);

/// <summary>
/// Almacén geoespacial de posiciones de repartidores (Redis, FR-003.3).
/// </summary>
public interface ITrackingStore
{
    /// <summary>Registra la posición del repartidor y renueva el TTL de la clave.</summary>
    Task ActualizarAsync(Guid riderId, double latitud, double longitud, DateTime timestamp, CancellationToken cancellationToken = default);

    /// <summary>Posición exacta de un repartidor, o null si no hay telemetría vigente.</summary>
    Task<UbicacionGps?> ObtenerAsync(Guid riderId, CancellationToken cancellationToken = default);

    /// <summary>Repartidor más cercano dentro del radio indicado, con su distancia en metros.</summary>
    Task<UbicacionGps?> BuscarCercanoAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Todos los repartidores dentro del radio, ordenados por distancia ascendente.
    ///
    /// La asignación automática necesita la lista y no solo el primero: el más cercano
    /// puede estar ocupado con otra entrega, y entonces corresponde el siguiente.
    /// </summary>
    Task<IReadOnlyList<UbicacionGps>> BuscarCandidatosAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default);
}
