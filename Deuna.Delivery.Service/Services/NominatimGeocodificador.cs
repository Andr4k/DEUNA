using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Deuna.Delivery.Service.Services;

/// <summary>Configuración del geocodificador (Nominatim).</summary>
public class GeocodificacionOptions
{
    public const string Seccion = "Geocodificacion";

    public string UrlBase { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>
    /// Identificación que exige Nominatim. Tiene que decir quién es el cliente: el
    /// User-Agent por defecto de HttpClient es genérico y Nominatim bloquea a quien no se
    /// identifica.
    /// </summary>
    public string UserAgent { get; set; } = "deuna-delivery-mapa/1.0 (+https://deuna.local)";

    /// <summary>Cuánto vale un resultado. La dirección de una sede no cambia de un día para otro.</summary>
    public int CacheSegundos { get; set; } = 86400;

    /// <summary>
    /// Espacio mínimo entre pedidos al servicio externo. Nominatim es gratis pero tiene
    /// límite de uso: sin este intervalo, el primer request con la flota completa dispara
    /// una ráfaga y el servicio responde 403.
    /// </summary>
    public int IntervaloMinimoMs { get; set; } = 1000;
}

/// <summary>
/// Geocodifica contra Nominatim, el geocoder de OpenStreetMap — el mismo ecosistema que
/// usa el mapa en el portal, así que las coordenadas son consistentes con las teselas.
///
/// Cachea el resultado, incluido el fallo: el portal refresca cada 10 segundos y sin caché
/// cada refresco le pegaría al servicio externo por cada restaurante.
/// </summary>
public class NominatimGeocodificador : IGeocodificador
{
    private readonly HttpClient _cliente;
    private readonly IMemoryCache _cache;
    private readonly GeocodificacionOptions _options;
    private readonly ILogger<NominatimGeocodificador> _logger;

    /// <summary>
    /// Serializa los pedidos al servicio externo. Es un semáforo y no un <c>Task.Delay</c>
    /// suelto porque el intervalo solo sirve si nadie más puede colarse entre dos pedidos.
    /// </summary>
    private readonly SemaphoreSlim _compuerta = new(1, 1);

    public NominatimGeocodificador(
        HttpClient cliente,
        IMemoryCache cache,
        IOptions<GeocodificacionOptions> options,
        ILogger<NominatimGeocodificador> logger)
    {
        _cliente = cliente;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Coordenada?> GeocodificarAsync(
        string direccion,
        string ciudad,
        CancellationToken cancellationToken = default)
    {
        var clave = ClaveCache(direccion, ciudad);

        if (_cache.TryGetValue(clave, out Coordenada? cacheada))
        {
            return cacheada;
        }

        await _compuerta.WaitAsync(cancellationToken);
        try
        {
            // Otra request pudo resolver la misma dirección mientras se esperaba la compuerta.
            if (_cache.TryGetValue(clave, out cacheada))
            {
                return cacheada;
            }

            var coordenada = await ConsultarAsync(direccion, ciudad, cancellationToken);

            // Se cachea también el null: si la dirección no existe, no tiene sentido volver a
            // preguntar en cada refresco del mapa.
            _cache.Set(clave, coordenada, TimeSpan.FromSeconds(_options.CacheSegundos));

            await Task.Delay(_options.IntervaloMinimoMs, cancellationToken);

            return coordenada;
        }
        finally
        {
            _compuerta.Release();
        }
    }

    private async Task<Coordenada?> ConsultarAsync(
        string direccion,
        string ciudad,
        CancellationToken cancellationToken)
    {
        var consulta = Uri.EscapeDataString($"{direccion}, {ciudad}");
        var url = $"{_options.UrlBase.TrimEnd('/')}/search?q={consulta}&format=jsonv2&limit=1";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var respuesta = await _cliente.SendAsync(request, cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Nominatim respondió {Codigo} al geocodificar \"{Direccion}, {Ciudad}\"",
                    (int)respuesta.StatusCode, direccion, ciudad);
                return null;
            }

            var resultados = await respuesta.Content
                .ReadFromJsonAsync<List<NominatimResultado>>(cancellationToken: cancellationToken);

            var primero = resultados?.FirstOrDefault();

            if (primero is null
                || !double.TryParse(primero.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitud)
                || !double.TryParse(primero.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitud))
            {
                _logger.LogWarning(
                    "Nominatim no ubicó \"{Direccion}, {Ciudad}\"", direccion, ciudad);
                return null;
            }

            return new Coordenada(latitud, longitud);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Un geocoder caído no puede tumbar el mapa: la capa de restaurantes se queda sin
            // ese punto, se loguea y el operador sigue viendo el resto de la operación.
            _logger.LogWarning(
                ex, "No se pudo geocodificar \"{Direccion}, {Ciudad}\"", direccion, ciudad);
            return null;
        }
    }

    private static string ClaveCache(string direccion, string ciudad) =>
        $"geocodificacion:{direccion.Trim()}|{ciudad.Trim()}".ToLowerInvariant();

    /// <summary>Respuesta cruda de <c>/search</c> de Nominatim (solo los campos que se usan).</summary>
    private sealed record NominatimResultado(
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon);
}
