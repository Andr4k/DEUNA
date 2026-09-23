using Deuna.Delivery.Service.Services;

namespace Deuna.Delivery.Service.Tests;

/// <summary>
/// Store de telemetría sin Redis, para los tests que arrancan la API completa.
///
/// Los tests HTTP no levantan Redis: el matching por cercanía tiene su propia suite con
/// Redis real (Assignment/ y Tracking/). Este doble existe para que el grafo de
/// dependencias sea resoluble y para que el comportamiento observable sea el correcto:
/// sin telemetría no hay candidatos, así que el pedido queda en búsqueda.
/// </summary>
internal sealed class TrackingStoreNulo : ITrackingStore
{
    public Task ActualizarAsync(Guid riderId, double latitud, double longitud, DateTime timestamp, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<UbicacionGps?> ObtenerAsync(Guid riderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<UbicacionGps?>(null);

    public Task<UbicacionGps?> BuscarCercanoAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default) =>
        Task.FromResult<UbicacionGps?>(null);

    public Task<IReadOnlyList<UbicacionGps>> BuscarCandidatosAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UbicacionGps>>([]);
}
