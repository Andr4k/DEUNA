using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Delivery.Service.Tests.Tracking;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Deuna.Delivery.Service.Tests.Assignment;

/// <summary>
/// Los candidatos para un pedido: de dónde sale la lista que el operador ve en el panel.
///
/// Devuelve lo que Delivery SABE. No incluye "libera en" ni una calificación del
/// repartidor: la primera no existe como dato en ningún servicio, y la segunda vive en
/// Feedback, no acá. Inventar cualquiera de las dos sería peor que no mostrarlas.
///
/// Redis real (TestContainers): la lista sale de GEORADIUS, así que un doble en memoria
/// probaría al doble y no al radio.
/// </summary>
[Collection(RedisCollection.Name)]
public class CandidatosTests : IDisposable
{
    private const double LatPedido = 4.6097100;
    private const double LonPedido = -74.0817500;

    private static readonly Guid RiderCercano = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid RiderMedio = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid RiderLejano = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
    private static readonly Guid RiderSinPerfil = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private readonly IConnectionMultiplexer _redis;
    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public CandidatosTests(RedisContainerFixture redis)
    {
        _redis = redis.CrearConexion();
        _db = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"candidatos-{Guid.NewGuid()}")
            .Options);
        _store = new RedisTrackingStore(_redis, NullLogger<RedisTrackingStore>.Instance);

        LimpiarTelemetria();
    }

    public void Dispose()
    {
        LimpiarTelemetria();
        _db.Dispose();
        _redis.Dispose();
    }

    private void LimpiarTelemetria()
    {
        var db = _redis.GetDatabase();
        db.KeyDelete(RedisTrackingStore.ClaveRepartidores);
        db.KeyDelete(RedisTrackingStore.ClaveTimestamps);
    }

    private IAsignacionService CrearServicio(double radioKm = 5) =>
        new AsignacionService(
            _db,
            _store,
            _publish.Object,
            Options.Create(new AsignacionOptions { RadioKm = radioKm }),
            NullLogger<AsignacionService>.Instance);

    private async Task UbicarAsync(Guid riderId, double kmAlNorte)
    {
        await _store.ActualizarAsync(
            riderId, LatPedido + (kmAlNorte / 111.32), LonPedido, DateTime.UtcNow);
    }

    private async Task<PedidoDisponible> CrearPedidoAsync(
        string estado = PedidoDisponible.EstadoBuscando,
        Guid? repartidorId = null)
    {
        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-CAND-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 55000m,
            Estado = estado,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            TokenQrEntrega = Guid.NewGuid().ToString("N"),
            Latitud = LatPedido,
            Longitud = LonPedido,
            RepartidorId = repartidorId,
            AsignadoAt = repartidorId is null ? null : DateTime.UtcNow.AddMinutes(-7)
        };

        _db.PedidosDisponibles.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido;
    }

    private async Task RegistrarRepartidorAsync(Guid riderId, string nombre, string? ciudad = "Bogota")
    {
        _db.RepartidoresReplicados.Add(new RepartidorReplicado
        {
            Id = riderId,
            NombreCompleto = nombre,
            CiudadOperacion = ciudad
        });

        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ la lista

    [Fact]
    public async Task ListaLosCandidatosDelRadioOrdenadosPorDistancia()
    {
        await UbicarAsync(RiderMedio, 3);
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Cercana");
        await RegistrarRepartidorAsync(RiderMedio, "Beto Medio");
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Exito.Should().BeTrue();
        resultado.Candidatos.Should().HaveCount(2);
        resultado.Candidatos![0].RepartidorId.Should().Be(RiderCercano, "el más cercano va primero");
        resultado.Candidatos[1].RepartidorId.Should().Be(RiderMedio);
    }

    [Fact]
    public async Task TraeElNombreYLaCiudadDesdeLaReplica()
    {
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Torres", "Medellin");
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        var candidato = resultado.Candidatos!.Single();
        candidato.NombreCompleto.Should().Be("Ana Torres");
        candidato.CiudadOperacion.Should().Be("Medellin");
    }

    [Fact]
    public async Task ExcluyeAQuienEstaFueraDelRadio()
    {
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderLejano, 25);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Cercana");
        await RegistrarRepartidorAsync(RiderLejano, "Zoe Lejana");
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Candidatos.Should().HaveCount(1);
        resultado.Candidatos!.Single().RepartidorId.Should().Be(RiderCercano);
    }

    [Fact]
    public async Task SinNadieEnElRadioDevuelveListaVaciaYNoUnError()
    {
        // Que no haya candidatos es una situación normal, no una falla: la pantalla tiene
        // que poder decir "no hay nadie cerca" sin que el endpoint le devuelva un error.
        await UbicarAsync(RiderLejano, 25);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Exito.Should().BeTrue();
        resultado.Candidatos.Should().BeEmpty();
    }

    // ------------------------------------------------------ disponible / ocupado

    [Fact]
    public async Task MarcaComoDisponibleAQuienNoTieneEntregaActiva()
    {
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Libre");
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        var candidato = resultado.Candidatos!.Single();
        candidato.Disponible.Should().BeTrue();
        candidato.ServicioActualCodigo.Should().BeNull();
        candidato.OcupadoDesde.Should().BeNull();
    }

    [Fact]
    public async Task MarcaComoNoDisponibleAQuienYaTieneUnaEntrega()
    {
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Ocupada");
        var enCurso = await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        var candidato = resultado.Candidatos!.Single();
        candidato.Disponible.Should().BeFalse();
        candidato.ServicioActualCodigo.Should().Be(enCurso.Codigo, "el operador necesita saber en qué anda");
        candidato.ServicioActualEstado.Should().Be(PedidoDisponible.EstadoEnRuta);
    }

    [Fact]
    public async Task DiceDesdeCuandoEstaOcupado()
    {
        // No es "libera en X minutos": eso no existe como dato. Es desde cuándo está
        // ocupado, que sí es un hecho, y el operador decide con eso.
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Ocupada");
        await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RiderCercano);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        var candidato = resultado.Candidatos!.Single();
        candidato.OcupadoDesde.Should().NotBeNull();
        candidato.OcupadoDesde.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task UnPedidoEntregadoNoOcupaAlRepartidor()
    {
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Libre");
        await CrearPedidoAsync(PedidoDisponible.EstadoEntregado, RiderCercano);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Candidatos!.Single().Disponible.Should().BeTrue("una entrega terminada no lo ocupa");
    }

    [Fact]
    public async Task ListaAQuienTieneTelemetriaPeroNoEstaEnLaReplica()
    {
        // Sale en la lista con el nombre vacío en vez de desaparecer: es un repartidor
        // reportando posición, y esconderlo dejaría al operador sin poder asignarle.
        await UbicarAsync(RiderSinPerfil, 2);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        var candidato = resultado.Candidatos!.Single();
        candidato.RepartidorId.Should().Be(RiderSinPerfil);
        candidato.NombreCompleto.Should().BeNull();
    }

    // -------------------------------------------------------------- los rechazos

    [Fact]
    public async Task RechazaUnPedidoQueYaNoEstaEnBuscando()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RiderLejano);

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Exito.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.EstadoInvalido);
        resultado.Candidatos.Should().BeNull();
    }

    [Fact]
    public async Task RechazaUnPedidoSinPuntoDeEntrega()
    {
        var pedido = await CrearPedidoAsync();
        pedido.Latitud = null;
        pedido.Longitud = null;
        await _db.SaveChangesAsync();

        var resultado = await CrearServicio().ObtenerCandidatosAsync(pedido.PedidoId);

        resultado.Exito.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.SinPuntoEntrega);
    }

    [Fact]
    public async Task RechazaUnPedidoQueNoEstaProyectado()
    {
        var resultado = await CrearServicio().ObtenerCandidatosAsync(Guid.NewGuid());

        resultado.Exito.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.PedidoNoEncontrado);
    }
}
