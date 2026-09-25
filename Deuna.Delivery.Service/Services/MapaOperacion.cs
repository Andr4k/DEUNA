using Deuna.Delivery.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Deuna.Delivery.Service.Services;

/// <summary>Configuración del mapa de la operación.</summary>
public class MapaOptions
{
    public const string Seccion = "Mapa";

    /// <summary>
    /// Cuánto vale una posición sin refrescar.
    ///
    /// La telemetría vive una hora en Redis, pero una hora de antigüedad no es una posición
    /// "en vivo": el portal refresca cada 10 segundos y el operador decide con lo que ve.
    /// Pasado este umbral el domiciliario se sigue mostrando, marcado como no vigente.
    /// </summary>
    public int TelemetriaVigenteSegundos { get; set; } = 120;
}

/// <summary>
/// Un domiciliario en el mapa.
///
/// <see cref="Vigente"/> no es decorativo: un domiciliario con telemetría vieja se muestra
/// igual, pero marcado. Ocultarlo haría que el operador creyera que no tiene flota cuando
/// en realidad tiene domiciliarios que dejaron de reportar.
/// </summary>
public record DomiciliarioEnMapa(
    Guid RepartidorId,
    string? Nombre,
    double Latitud,
    double Longitud,
    bool Vigente,
    bool Ocupado,
    string? ServicioActualCodigo);

/// <summary>Punto de entrega de un pedido activo, con su estado.</summary>
public record PedidoEnMapa(
    Guid PedidoId,
    string Codigo,
    double Latitud,
    double Longitud,
    string Estado);

/// <summary>Un restaurante ya ubicado por su dirección.</summary>
public record RestauranteEnMapa(
    Guid RestauranteId,
    string Nombre,
    double Latitud,
    double Longitud);

/// <summary>
/// Las tres capas del mapa de la operación.
///
/// <see cref="DomiciliariosRegistrados"/> es la flota conocida, no la que está reportando:
/// es lo que permite distinguir "no hay posiciones" de "no hay domiciliarios". Sin ese dato
/// las dos situaciones se ven igual —una lista vacía— y el operador cree que no tiene flota.
/// </summary>
public record MapaOperacion(
    IReadOnlyList<DomiciliarioEnMapa> Domiciliarios,
    IReadOnlyList<PedidoEnMapa> Pedidos,
    IReadOnlyList<RestauranteEnMapa> Restaurantes,
    int DomiciliariosRegistrados);

/// <summary>
/// Arma el mapa de la operación para el panel del administrador: domiciliarios en vivo
/// (Redis), pedidos activos (read-model local) y restaurantes ubicados por su dirección.
/// </summary>
public interface IMapaService
{
    Task<MapaOperacion> ObtenerAsync(CancellationToken cancellationToken = default);
}

public class MapaService : IMapaService
{
    /// <summary>
    /// Estados en los que un pedido sigue siendo parte de la operación: los que están
    /// esperando domiciliario y los que ocupan a uno. Entregado y Cancelado ya no están en
    /// la calle, así que no se dibujan.
    /// </summary>
    private static readonly string[] EstadosActivos =
        [PedidoDisponible.EstadoBuscando, .. PedidoDisponible.EstadosQueOcupanAlRepartidor];

    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly IGeocodificador _geocodificador;
    private readonly IMemoryCache _cache;
    private readonly GeocodificacionOptions _geocodificacion;
    private readonly MapaOptions _options;
    private readonly ILogger<MapaService> _logger;

    public MapaService(
        DeliveryDbContext db,
        ITrackingStore store,
        IGeocodificador geocodificador,
        IMemoryCache cache,
        IOptions<MapaOptions> options,
        IOptions<GeocodificacionOptions> geocodificacion,
        ILogger<MapaService> logger)
    {
        _db = db;
        _store = store;
        _geocodificador = geocodificador;
        _cache = cache;
        _geocodificacion = geocodificacion.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MapaOperacion> ObtenerAsync(CancellationToken cancellationToken = default)
    {
        var posiciones = await _store.ObtenerTodasAsync(cancellationToken);

        // La flota se lee completa y no solo los que están en el GEO set: sin esto no se
        // puede saber si la lista vacía significa "nadie reporta" o "no hay a quién esperar".
        var flota = await _db.RepartidoresReplicados
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var nombres = flota.ToDictionary(r => r.Id, r => r.NombreCompleto);

        var pedidosActivos = await _db.PedidosDisponibles
            .AsNoTracking()
            .Where(p => EstadosActivos.Contains(p.Estado))
            .ToListAsync(cancellationToken);

        var domiciliarios = ArmarDomiciliarios(posiciones, nombres, pedidosActivos);

        var pedidos = pedidosActivos
            .Where(p => p.Latitud is not null && p.Longitud is not null)
            .Select(p => new PedidoEnMapa(p.PedidoId, p.Codigo, p.Latitud!.Value, p.Longitud!.Value, p.Estado))
            .ToList();

        var restaurantes = await UbicarRestaurantesAsync(cancellationToken);

        _logger.LogInformation(
            "Mapa de la operación: {Domiciliarios} domiciliario(s) con posición ({Vigentes} vigentes) de {Flota} registrados, {Pedidos} pedido(s) activo(s), {Restaurantes} restaurante(s) ubicado(s)",
            domiciliarios.Count, domiciliarios.Count(d => d.Vigente), flota.Count, pedidos.Count, restaurantes.Count);

        return new MapaOperacion(domiciliarios, pedidos, restaurantes, flota.Count);
    }

    private List<DomiciliarioEnMapa> ArmarDomiciliarios(
        IReadOnlyList<UbicacionGps> posiciones,
        Dictionary<Guid, string> nombres,
        List<PedidoDisponible> pedidosActivos)
    {
        // Ocupado = tiene una entrega activa. Se resuelve contra los pedidos ya cargados, sin
        // una consulta por domiciliario.
        var servicios = pedidosActivos
            .Where(p => p.RepartidorId is not null
                        && PedidoDisponible.EstadosQueOcupanAlRepartidor.Contains(p.Estado))
            .GroupBy(p => p.RepartidorId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var vigenteDesde = DateTime.UtcNow.AddSeconds(-_options.TelemetriaVigenteSegundos);

        return posiciones
            .Select(p => new DomiciliarioEnMapa(
                RepartidorId: p.RiderId,
                Nombre: nombres.GetValueOrDefault(p.RiderId),
                Latitud: p.Latitud,
                Longitud: p.Longitud,
                // Sin timestamp no hay forma de afirmar que la posición sigue siendo cierta.
                Vigente: p.ActualizadoEn is not null && p.ActualizadoEn.Value >= vigenteDesde,
                Ocupado: servicios.ContainsKey(p.RiderId),
                ServicioActualCodigo: servicios.GetValueOrDefault(p.RiderId)?.Codigo))
            .ToList();
    }

    private async Task<IReadOnlyList<RestauranteEnMapa>> UbicarRestaurantesAsync(CancellationToken cancellationToken)
    {
        var restaurantes = await _db.RestaurantesReplicados
            .AsNoTracking()
            .Where(r => r.Activo)
            .ToListAsync(cancellationToken);

        var ubicados = new List<RestauranteEnMapa>(restaurantes.Count);

        foreach (var restaurante in restaurantes)
        {
            var coordenada = await GeocodificarConCacheAsync(
                restaurante.DireccionSede, restaurante.Ciudad, cancellationToken);

            if (coordenada is null)
            {
                // Se omite y se loguea. Inventarle una coordenada pondría un marcador falso en
                // el mapa y el operador decidiría con un dato que no existe.
                _logger.LogWarning(
                    "Restaurante {RestauranteId} ({Nombre}) queda fuera del mapa: no se pudo ubicar \"{Direccion}, {Ciudad}\"",
                    restaurante.Id, restaurante.NombreComercial, restaurante.DireccionSede, restaurante.Ciudad);
                continue;
            }

            ubicados.Add(new RestauranteEnMapa(
                restaurante.Id, restaurante.NombreComercial, coordenada.Value.Latitud, coordenada.Value.Longitud));
        }

        return ubicados;
    }

    /// <summary>
    /// Ubica una dirección reutilizando el resultado anterior.
    ///
    /// El portal refresca el mapa cada 10 segundos y la dirección de una sede no cambia: sin
    /// esta caché cada refresco le pegaría a Nominatim por cada restaurante y el límite de uso
    /// del servicio terminaría bloqueando al mapa entero. Se cachea también el fallo (null)
    /// para no volver a preguntar por una dirección que no existe.
    /// </summary>
    private async Task<Coordenada?> GeocodificarConCacheAsync(
        string direccion, string ciudad, CancellationToken cancellationToken)
    {
        var clave = $"mapa:geocodificacion:{direccion.Trim()}|{ciudad.Trim()}".ToLowerInvariant();

        if (_cache.TryGetValue(clave, out Coordenada? cacheada))
        {
            return cacheada;
        }

        var coordenada = await _geocodificador.GeocodificarAsync(direccion, ciudad, cancellationToken);

        _cache.Set(clave, coordenada, TimeSpan.FromSeconds(_geocodificacion.CacheSegundos));

        return coordenada;
    }
}
