using Deuna.Delivery.Service.Services;

namespace Deuna.Delivery.Service.Tests.Tracking;

/// <summary>
/// Geocodificador de prueba: el real pega contra Nominatim, que es un servicio externo con
/// límite de uso y sin red garantizada en los tests.
///
/// Solo resuelve las direcciones que el test declaró: lo que no se declaró se comporta
/// como una dirección que Nominatim no pudo ubicar, que es el caso que hay que cubrir.
/// </summary>
internal sealed class GeocodificadorFalso : IGeocodificador
{
    private readonly Dictionary<string, Coordenada> _resueltas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cuántas veces se le pidió una dirección: sirve para probar el cacheo.</summary>
    public int Llamadas { get; private set; }

    /// <summary>Direcciones que este doble sabe ubicar.</summary>
    public IReadOnlyCollection<string> Resueltas => _resueltas.Keys;

    public void Resolver(string direccion, string ciudad, double latitud, double longitud) =>
        _resueltas[Clave(direccion, ciudad)] = new Coordenada(latitud, longitud);

    public Task<Coordenada?> GeocodificarAsync(string direccion, string ciudad, CancellationToken cancellationToken = default)
    {
        Llamadas++;

        return Task.FromResult(
            _resueltas.TryGetValue(Clave(direccion, ciudad), out var coordenada)
                ? coordenada
                : (Coordenada?)null);
    }

    private static string Clave(string direccion, string ciudad) => $"{direccion}, {ciudad}";
}
