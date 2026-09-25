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

    /// <summary>
    /// Tope de candidatos que devuelve el matching. Existe para no traer un radio entero
    /// de repartidores cuando el pedido se resuelve con los primeros por cercanía.
    /// </summary>
    public const int MaxCandidatos = 50;

    /// <summary>
    /// Radio de la búsqueda sin centro que usa el mapa. La distancia máxima entre dos
    /// puntos de la Tierra es media circunferencia (~20 015 km), así que cualquier radio
    /// mayor cubre el planeta entero y devuelve la flota completa esté donde esté.
    /// </summary>
    public const double RadioGlobalKm = 25000;

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

    /// <summary>
    /// Borra la posición y el timestamp del repartidor. La clave GEO es un sorted set, así
    /// que se saca el miembro con ZREM en lugar de borrar la clave entera: otros
    /// repartidores siguen en ella.
    /// </summary>
    public async Task EliminarAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var miembro = riderId.ToString();

        await db.SortedSetRemoveAsync(ClaveRepartidores, miembro);
        await db.HashDeleteAsync(ClaveTimestamps, miembro);

        _logger.LogInformation(
            "Telemetría GPS del repartidor {RiderId} eliminada tras cerrar la entrega", riderId);
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

    public async Task<IReadOnlyList<UbicacionGps>> BuscarCandidatosAsync(double latitud, double longitud, double radioKm, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // GEORADIUS tracking:riders {lon} {lat} {radio} km WITHDIST ASC
        var resultados = await db.GeoRadiusAsync(
            ClaveRepartidores,
            longitude: longitud,
            latitude: latitud,
            radius: radioKm,
            unit: GeoUnit.Kilometers,
            count: MaxCandidatos,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance);

        if (resultados is null || resultados.Length == 0)
        {
            return [];
        }

        // Los timestamps se leen de una sola vez: pedirlos por candidato sería una
        // ida y vuelta a Redis por cada repartidor del radio.
        var timestamps = (await db.HashGetAllAsync(ClaveTimestamps))
            .ToDictionary(h => h.Name.ToString(), h => ParsearTimestamp(h.Value));

        var candidatos = new List<UbicacionGps>(resultados.Length);
        foreach (var resultado in resultados)
        {
            if (resultado.Position is null)
            {
                continue;
            }

            var miembro = resultado.Member.ToString();
            if (!Guid.TryParse(miembro, out var riderId))
            {
                _logger.LogWarning("Miembro no interpretable como Guid en {Clave}: {Miembro}", ClaveRepartidores, miembro);
                continue;
            }

            candidatos.Add(new UbicacionGps(
                riderId,
                resultado.Position.Value.Latitude,
                resultado.Position.Value.Longitude,
                DistanciaMetros: resultado.Distance * 1000, // GEORADIUS devuelve km
                ActualizadoEn: timestamps.TryGetValue(miembro, out var timestamp) ? timestamp : null));
        }

        return candidatos;
    }

    /// <summary>
    /// Todas las posiciones del GEO set, con el timestamp de cada una si lo tienen.
    ///
    /// Es GEORADIUS con un radio global y no un recorrido del sorted set: se aprovecha la
    /// consulta geoespacial nativa, en una sola ida y vuelta a Redis.
    /// </summary>
    public async Task<IReadOnlyList<UbicacionGps>> ObtenerTodasAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        var resultados = await db.GeoRadiusAsync(
            ClaveRepartidores,
            longitude: 0,
            latitude: 0,
            radius: RadioGlobalKm,
            unit: GeoUnit.Kilometers,
            options: GeoRadiusOptions.WithCoordinates);

        if (resultados is null || resultados.Length == 0)
        {
            return [];
        }

        // Igual que en BuscarCandidatosAsync: los timestamps se leen de una sola vez.
        var timestamps = (await db.HashGetAllAsync(ClaveTimestamps))
            .ToDictionary(h => h.Name.ToString(), h => ParsearTimestamp(h.Value));

        var ubicaciones = new List<UbicacionGps>(resultados.Length);
        foreach (var resultado in resultados)
        {
            if (resultado.Position is null)
            {
                continue;
            }

            var miembro = resultado.Member.ToString();
            if (!Guid.TryParse(miembro, out var riderId))
            {
                _logger.LogWarning("Miembro no interpretable como Guid en {Clave}: {Miembro}", ClaveRepartidores, miembro);
                continue;
            }

            ubicaciones.Add(new UbicacionGps(
                riderId,
                resultado.Position.Value.Latitude,
                resultado.Position.Value.Longitude,
                DistanciaMetros: null,
                ActualizadoEn: timestamps.TryGetValue(miembro, out var timestamp) ? timestamp : null));
        }

        return ubicaciones;
    }

    private static async Task<DateTime?> LeerTimestampAsync(IDatabase db, string miembro)
    {
        var valor = await db.HashGetAsync(ClaveTimestamps, miembro);
        return valor.HasValue ? ParsearTimestamp(valor) : null;
    }

    private static DateTime? ParsearTimestamp(RedisValue valor) =>
        valor.HasValue && DateTime.TryParse(valor.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : null;
}
