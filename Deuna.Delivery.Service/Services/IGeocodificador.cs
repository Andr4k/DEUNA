namespace Deuna.Delivery.Service.Services;

/// <summary>Coordenada geográfica en WGS84, tal como la devuelve el geocodificador.</summary>
public readonly record struct Coordenada(double Latitud, double Longitud);

/// <summary>
/// Resuelve una dirección de texto a coordenadas (WGS84).
///
/// Es una interfaz y no una llamada directa porque la única implementación le pega a un
/// servicio externo (Nominatim) que puede no estar disponible: los tests necesitan poder
/// sustituirlo, y un fallo tiene que poder tratarse como "no se pudo ubicar" en vez de
/// tumbar la respuesta entera.
/// </summary>
public interface IGeocodificador
{
    /// <summary>Coordenadas de la dirección, o null si no se pudo resolver.</summary>
    Task<Coordenada?> GeocodificarAsync(string direccion, string ciudad, CancellationToken cancellationToken = default);
}
