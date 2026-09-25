using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Deuna.Delivery.Service.Tests.Tracking;

/// <summary>
/// El mapa de la operación: las tres capas —domiciliarios, pedidos y restaurantes— en una
/// sola respuesta para el panel del administrador.
///
/// Redis real (TestContainers) porque el mapa se alimenta del GEO set y el hash de
/// timestamps es lo que decide si una posición sigue vigente: con un doble en memoria se
/// probaría el doble, no la consulta.
///
/// El geocodificador se reemplaza por <see cref="GeocodificadorFalso"/>: Nominatim es un
/// servicio externo y los tests no pueden depender de que haya red.
/// </summary>
[Collection(RedisCollection.Name)]
public class MapaOperacionTests : IAsyncLifetime
{
    private const string RutaMapa = "/api/v1/admin/delivery/mapa";

    private static readonly Guid RiderVigente = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid RiderViejo = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

    private const double LatBase = 4.6097;
    private const double LonBase = -74.0817;

    private readonly RedisContainerFixture _fixture;
    private readonly GeocodificadorFalso _geocodificador = new();
    private readonly IConnectionMultiplexer _redis;

    private TrackingWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public MapaOperacionTests(RedisContainerFixture fixture)
    {
        _fixture = fixture;
        _redis = fixture.CrearConexion();
    }

    public async Task InitializeAsync()
    {
        _factory = new TrackingWebApplicationFactory(_fixture.ConnectionString, _geocodificador);
        _client = _factory.CreateClient();
        await LimpiarAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _redis.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>La BD InMemory se comparte por nombre dentro del proceso: se limpia entre tests.</summary>
    private async Task LimpiarAsync()
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        db.PedidosDisponibles.RemoveRange(db.PedidosDisponibles);
        db.RepartidoresReplicados.RemoveRange(db.RepartidoresReplicados);
        db.RestaurantesReplicados.RemoveRange(db.RestaurantesReplicados);
        await db.SaveChangesAsync();

        var redis = _redis.GetDatabase();
        await redis.KeyDeleteAsync(RedisTrackingStore.ClaveRepartidores);
        await redis.KeyDeleteAsync(RedisTrackingStore.ClaveTimestamps);
    }

    // ------------------------------------------------------------------- siembra

    /// <summary>
    /// Escribe la telemetría cruda en Redis. Se hace a mano —y no con
    /// <c>ITrackingStore.ActualizarAsync</c>— porque el timestamp es justamente lo que hay
    /// que controlar: la telemetría vieja no se puede fabricar con el reloj del sistema.
    /// Un timestamp null deja al repartidor en el GEO set sin entrada en el hash.
    /// </summary>
    private async Task UbicarAsync(Guid riderId, double latitud, double longitud, DateTime? timestamp)
    {
        var db = _redis.GetDatabase();
        var miembro = riderId.ToString();

        await db.GeoAddAsync(RedisTrackingStore.ClaveRepartidores, new GeoEntry(longitud, latitud, miembro));

        if (timestamp is null)
        {
            await db.HashDeleteAsync(RedisTrackingStore.ClaveTimestamps, miembro);
            return;
        }

        await db.HashSetAsync(
            RedisTrackingStore.ClaveTimestamps, miembro, timestamp.Value.ToUniversalTime().ToString("O"));
    }

    private async Task SembrarRepartidorAsync(Guid riderId, string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        db.RepartidoresReplicados.Add(new RepartidorReplicado
        {
            Id = riderId,
            NombreCompleto = nombre,
            CiudadOperacion = "Bogota"
        });

        await db.SaveChangesAsync();
    }

    private async Task SembrarRestauranteAsync(Guid restauranteId, string nombre, string direccion, string ciudad)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        db.RestaurantesReplicados.Add(new RestauranteReplicado
        {
            Id = restauranteId,
            NombreComercial = nombre,
            DireccionSede = direccion,
            Ciudad = ciudad
        });

        await db.SaveChangesAsync();
    }

    private async Task<PedidoDisponible> SembrarPedidoAsync(
        string estado = PedidoDisponible.EstadoBuscando,
        double? latitud = LatBase,
        double? longitud = LonBase,
        Guid? repartidorId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-MAPA-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 55000m,
            Estado = estado,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            TokenQrEntrega = Guid.NewGuid().ToString("N"),
            Latitud = latitud,
            Longitud = longitud,
            RepartidorId = repartidorId
        };

        db.PedidosDisponibles.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    // ------------------------------------------------------------------- acceso

    private HttpClient ClienteConToken(string token)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private HttpClient ClienteAdmin() => ClienteConToken(
        TrackingWebApplicationFactory.CrearToken(Guid.NewGuid(), Roles.Admin));

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private async Task<(HttpStatusCode Codigo, RespuestaMapa? Cuerpo)> PedirMapaAsync(HttpClient cliente)
    {
        var respuesta = await cliente.GetAsync(RutaMapa);

        // Un 401 y un 403 salen SIN cuerpo: deserializar a ciegas reventaría con "the input
        // does not contain any JSON tokens" y el test fallaría por el motivo equivocado.
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = string.IsNullOrWhiteSpace(texto)
            ? null
            : JsonSerializer.Deserialize<RespuestaMapa>(texto, JsonOpciones);

        return (respuesta.StatusCode, cuerpo);
    }

    [Fact]
    public async Task Mapa_SinToken_Devuelve401()
    {
        var respuesta = await _client.GetAsync(RutaMapa);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mapa_ConTokenDeRepartidor_Devuelve403()
    {
        // La ruta vive en el grupo "admin": un domiciliario con credenciales válidas no ve
        // el mapa de la operación.
        using var cliente = ClienteConToken(
            TrackingWebApplicationFactory.CrearToken(Guid.NewGuid(), Roles.Rider));

        var respuesta = await cliente.GetAsync(RutaMapa);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------ domiciliarios

    [Fact]
    public async Task Mapa_DomiciliarioConTelemetriaVieja_SaleNoVigenteYNoSeOculta()
    {
        await UbicarAsync(RiderVigente, LatBase, LonBase, DateTime.UtcNow);
        await UbicarAsync(RiderViejo, LatBase + 0.01, LonBase, DateTime.UtcNow.AddMinutes(-30));
        await SembrarRepartidorAsync(RiderVigente, "Ana Vigente");
        await SembrarRepartidorAsync(RiderViejo, "Beto Viejo");
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirMapaAsync(cliente);

        codigo.Should().Be(HttpStatusCode.OK);

        // Los dos aparecen: al de telemetría vieja se lo marca, no se lo esconde. Ocultarlo
        // haría que el operador creyera que ese domiciliario no existe.
        cuerpo!.Domiciliarios.Should().HaveCount(2);

        var vigente = cuerpo.Domiciliarios.Single(d => d.RepartidorId == RiderVigente);
        vigente.Vigente.Should().BeTrue();
        vigente.Nombre.Should().Be("Ana Vigente");
        vigente.Latitud.Should().BeApproximately(LatBase, 0.0001);

        var viejo = cuerpo.Domiciliarios.Single(d => d.RepartidorId == RiderViejo);
        viejo.Vigente.Should().BeFalse("su última telemetría tiene media hora");
        viejo.Latitud.Should().BeApproximately(LatBase + 0.01, 0.0001, "la última posición conocida sigue siendo un dato");
    }

    [Fact]
    public async Task Mapa_DomiciliarioSinTimestamp_SaleNoVigente()
    {
        // Está en el GEO set (tiene posición) pero no en el hash de timestamps: sin
        // timestamp no hay forma de afirmar que la posición sigue siendo cierta.
        await UbicarAsync(RiderViejo, LatBase, LonBase, timestamp: null);
        await SembrarRepartidorAsync(RiderViejo, "Beto Sin Hora");
        using var cliente = ClienteAdmin();

        var (_, cuerpo) = await PedirMapaAsync(cliente);

        var domiciliario = cuerpo!.Domiciliarios.Single();
        domiciliario.Vigente.Should().BeFalse();
        domiciliario.Latitud.Should().BeApproximately(LatBase, 0.0001);
    }

    [Fact]
    public async Task Mapa_DomiciliarioOcupado_SaleConSuServicioActual()
    {
        await UbicarAsync(RiderVigente, LatBase, LonBase, DateTime.UtcNow);
        await SembrarRepartidorAsync(RiderVigente, "Ana Ocupada");
        var enRuta = await SembrarPedidoAsync(
            PedidoDisponible.EstadoEnRuta, LatBase, LonBase, RiderVigente);
        using var cliente = ClienteAdmin();

        var (_, cuerpo) = await PedirMapaAsync(cliente);

        var domiciliario = cuerpo!.Domiciliarios.Single();
        domiciliario.Ocupado.Should().BeTrue();
        domiciliario.ServicioActualCodigo.Should().Be(enRuta.Codigo);
    }

    [Fact]
    public async Task Mapa_SinPosicionesPeroConFlota_DistingueVacioDeSinFlota()
    {
        // "No hay posiciones" y "no hay domiciliarios" no son lo mismo: si el mapa los
        // muestra igual, el operador cree que no tiene flota cuando en realidad tiene
        // domiciliarios que no están reportando.
        await SembrarRepartidorAsync(RiderVigente, "Ana Sin Telemetria");
        using var cliente = ClienteAdmin();

        var (_, cuerpo) = await PedirMapaAsync(cliente);

        cuerpo!.Domiciliarios.Should().BeEmpty();
        cuerpo.DomiciliariosRegistrados.Should().Be(1, "la flota existe: lo que falta es telemetría");
    }

    // ------------------------------------------------------------------ pedidos

    [Fact]
    public async Task Mapa_PedidoTerminal_NoAparece()
    {
        var buscando = await SembrarPedidoAsync(PedidoDisponible.EstadoBuscando);
        var asignado = await SembrarPedidoAsync(PedidoDisponible.EstadoAsignado);
        await SembrarPedidoAsync(PedidoDisponible.EstadoEntregado);
        await SembrarPedidoAsync(PedidoDisponible.EstadoCancelado);

        // Un pedido activo pero sin punto de entrega no se puede dibujar: se omite.
        await SembrarPedidoAsync(PedidoDisponible.EstadoBuscando, latitud: null, longitud: null);
        using var cliente = ClienteAdmin();

        var (_, cuerpo) = await PedirMapaAsync(cliente);

        cuerpo!.Pedidos.Should().HaveCount(2);
        cuerpo.Pedidos.Select(p => p.PedidoId).Should().BeEquivalentTo([buscando.PedidoId, asignado.PedidoId]);
        cuerpo.Pedidos.Select(p => p.Estado).Should().NotContain(PedidoDisponible.EstadoEntregado);
        cuerpo.Pedidos.Should().OnlyContain(p => p.Latitud != null && p.Longitud != null);
    }

    // ------------------------------------------------------------- restaurantes

    [Fact]
    public async Task Mapa_RestauranteSinGeocodificar_SeOmiteYNoRompeLaRespuesta()
    {
        // El restaurante se ubica por su dirección (es fijo). Si Nominatim no la puede
        // resolver, se omite y se loguea: inventarle una coordenada pondría un marcador
        // falso en el mapa y el operador decidiría con un dato que no existe.
        var ubicable = Guid.NewGuid();
        await SembrarRestauranteAsync(ubicable, "Resto Ubicable", "Calle 45 #23-10", "Bogota");
        await SembrarRestauranteAsync(Guid.NewGuid(), "Resto Perdido", "Dirección Inexistente 999", "Bogota");
        _geocodificador.Resolver("Calle 45 #23-10", "Bogota", 4.6481, -74.0911);
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirMapaAsync(cliente);

        codigo.Should().Be(HttpStatusCode.OK, "un restaurante que no se pudo ubicar no puede tumbar el mapa entero");
        cuerpo!.Restaurantes.Should().HaveCount(1);
        cuerpo.Restaurantes[0].RestauranteId.Should().Be(ubicable);
        cuerpo.Restaurantes[0].Nombre.Should().Be("Resto Ubicable");
        cuerpo.Restaurantes[0].Latitud.Should().BeApproximately(4.6481, 0.0001);
    }

    [Fact]
    public async Task Mapa_GeocodificaCadaRestauranteUnaSolaVez()
    {
        // Nominatim es gratis pero tiene límite de uso y exige un User-Agent identificable:
        // pedirle la misma dirección en cada request (el portal refresca cada 10 s) lo
        // termina bloqueando.
        await SembrarRestauranteAsync(Guid.NewGuid(), "Resto Cacheado", "Carrera 7 #45-12", "Bogota");
        _geocodificador.Resolver("Carrera 7 #45-12", "Bogota", 4.6533, -74.0622);
        using var cliente = ClienteAdmin();

        await PedirMapaAsync(cliente);
        var llamadasTrasLaPrimera = _geocodificador.Llamadas;
        var (_, cuerpo) = await PedirMapaAsync(cliente);

        llamadasTrasLaPrimera.Should().Be(1, "la primera consulta resuelve la dirección");
        _geocodificador.Llamadas.Should().Be(1, "la segunda sale del cache");
        cuerpo!.Restaurantes.Should().HaveCount(1);
    }
}

/// <summary>Forma de la respuesta del mapa, para poder leerla en los tests.</summary>
internal sealed record RespuestaMapa(
    List<DomiciliarioMapa> Domiciliarios,
    List<PedidoMapa> Pedidos,
    List<RestauranteMapa> Restaurantes,
    int DomiciliariosRegistrados);

internal sealed record DomiciliarioMapa(
    Guid RepartidorId,
    string? Nombre,
    double Latitud,
    double Longitud,
    bool Vigente,
    bool Ocupado,
    string? ServicioActualCodigo);

internal sealed record PedidoMapa(
    Guid PedidoId,
    string Codigo,
    double? Latitud,
    double? Longitud,
    string Estado);

internal sealed record RestauranteMapa(
    Guid RestauranteId,
    string Nombre,
    double Latitud,
    double Longitud);
