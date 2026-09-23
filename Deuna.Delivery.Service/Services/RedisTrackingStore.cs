using StackExchange.Redis;

namespace Deuna.Delivery.Service.Services;

/// <summary>
/// Almacén geoespacial de posiciones en Redis (US-003.3, FR-003.3).
///
/// Estructura:
///   tracking:riders             → GEO set con un miembro por repartidor  (GEOADD / GEORADIUS / GEOPOS)
///   tracking:riders:timestamps  → hash riderId → timestamp de la telemetría
///                                 (GEOADD no almacena el timestamp del payload)
///
/// Ambas claves llevan TTL de 1 hora: la telemetría vencida se limpia sola, sin DELETE.
/// </summary>
public class RedisTrackingStore : ITrackingStore
{
    /// <summary>Clave GEO única del plan técnico: GEOADD tracking:riders {lon} {lat} {riderId}.</summary>
    public const string ClaveRepartidores = "tracking:riders";

    /// <summary>Hash auxiliar con el timestamp reportado por cada repartidor.</summary>
    public const string ClaveTimestamps = "tracking:riders:timestamps";

    /// <summary>TTL de la telemetría (1 hora).</summary>
    public const int TtlSegundos = 3600;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTrackingStore> _logger;

    public RedisTrackingStore(IConnectionMultiplexer redis, ILogger<RedisTrackingStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task ActualizarAsync(Guid riderId, double latitud, double longitud, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var miembro = riderId.ToString();
        var ttl = TimeSpan.FromSeconds(TtlSegundos);

        // GEOADD usa (longitud, latitud): el orden inverso al del payload
        await db.GeoAddAsync(ClaveRepartidores, new GeoEntry(longitud, latitud, miembro));
        await db.KeyExpireAsync(ClaveRepartidores, ttl);

        await db.HashSetAsync(ClaveTimestamps, miembro, timestamp.ToUniversalTime().ToString("O"));
        await db.KeyExpireAsync(ClaveTimestamps, ttl);

        _logger.LogDebug(
            "Telemetría GPS registrada para el repartidor {RiderId} en {Latitud},{Longitud}", riderId, latitud, longitud);
    }

    public async Task<UbicacionGps?> ObtenerAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var miembro = riderId.ToString();

        var posicion = await db.GeoPositionAsync(ClaveRepartidores, miembro);
        if (posicion is null)
        {
            return null;
        }

        return new UbicacionGps(
            riderId,
            posicion.Value.Latitude,
            posicion.Value.Longitude,
            DistanciaMetros: null,
            ActualizadoEn: await LeerTimestampAsync(db, miembro));
    }

    public async Task<UbicacionGps?> BuscarCercanoAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // GEORADIUS tracking:riders {lon} {lat} {radio} km WITHDIST ASC COUNT 1
        var resultados = await db.GeoRadiusAsync(
            ClaveRepartidores,
            longitude: longitud,
            latitude: latitud,
            radius: radioKm,
            unit: GeoUnit.Kilometers,
            count: 1,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance);

        var masCercano = resultados?.FirstOrDefault();
        if (masCercano is null || masCercano.Value.Position is null)
        {
            return null;
        }

        var miembro = masCercano.Value.Member.ToString();
        if (!Guid.TryParse(miembro, out var riderId))
        {
            _logger.LogWarning("Miembro no interpretable como Guid en {Clave}: {Miembro}", ClaveRepartidores, miembro);
            return null;
        }

        return new UbicacionGps(
            riderId,
            masCercano.Value.Position.Value.Latitude,
            masCercano.Value.Position.Value.Longitude,
            DistanciaMetros: masCercano.Value.Distance * 1000, // GEORADIUS devuelve km
            ActualizadoEn: await LeerTimestampAsync(db, miembro));
    }

    private static async Task<DateTime?> LeerTimestampAsync(IDatabase db, string miembro)
    {
        var valor = await db.HashGetAsync(ClaveTimestamps, miembro);
        return valor.HasValue && DateTime.TryParse(valor.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : null;
    }
}
