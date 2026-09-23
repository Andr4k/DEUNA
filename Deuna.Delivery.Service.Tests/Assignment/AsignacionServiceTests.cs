using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Delivery.Service.Tests.Tracking;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Deuna.Delivery.Service.Tests.Assignment;

/// <summary>
/// TASK-303: asignación automática al domiciliario disponible más cercano.
///
/// El sistema busca y asigna; el domiciliario no toma pedidos de una lista (ADR-006).
/// Solo se consideran disponibles los que tienen telemetría vigente y no tienen una
/// entrega activa.
///
/// Redis real (TestContainers) porque el matching es GEORADIUS: con un doble en memoria
/// se probaría el doble, no la consulta geoespacial.
/// </summary>
[Collection(RedisCollection.Name)]
public class AsignacionServiceTests : IDisposable
{
    // Bogotá como referencia; los candidatos se ubican a distintas distancias al norte.
    private const double LatPedido = 4.6097100;
    private const double LonPedido = -74.0817500;

    private static readonly Guid RiderCercano = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RiderMedio = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid RiderLejano = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private readonly IConnectionMultiplexer _redis;
    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public AsignacionServiceTests(RedisContainerFixture redis)
    {
        _redis = redis.CrearConexion();
        _db = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"asignacion-{Guid.NewGuid()}")
            .Options);
        _store = new RedisTrackingStore(_redis, NullLogger<RedisTrackingStore>.Instance);

        // El contenedor es compartido por la colección: se limpia antes de cada test.
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

    /// <summary>Registra telemetría de un repartidor a cierta distancia al norte del pedido.</summary>
    private async Task UbicarAsync(Guid riderId, double kmAlNorte)
    {
        // 1 grado de latitud ≈ 111.32 km
        var latitud = LatPedido + (kmAlNorte / 111.32);
        await _store.ActualizarAsync(riderId, latitud, LonPedido, DateTime.UtcNow);
    }

    private async Task<PedidoDisponible> CrearPedidoAsync(string estado = PedidoDisponible.EstadoBuscando, Guid? repartidorId = null)
    {
        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..8]}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 55000m,
            Estado = estado,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            TokenQrEntrega = Guid.NewGuid().ToString("N"),
            Latitud = LatPedido,
            Longitud = LonPedido,
            RepartidorId = repartidorId
        };

        _db.PedidosDisponibles.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido;
    }

    // ---------------------------------------------------------------- asignación

    [Fact]
    public async Task AsignaAlRepartidorDisponibleMasCercano()
    {
        await UbicarAsync(RiderMedio, 3);
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderLejano, 4);
        var pedido = await CrearPedidoAsync();
        var servicio = CrearServicio();

        var resultado = await servicio.IntentarAsignarAsync(pedido.PedidoId);

        resultado.Asignado.Should().BeTrue();
        resultado.RepartidorId.Should().Be(RiderCercano, "es el más cercano de los tres");

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.Estado.Should().Be(PedidoDisponible.EstadoAsignado);
        actualizado.RepartidorId.Should().Be(RiderCercano);
        actualizado.AsignadoAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RegistraLaAsignacionConSuDistancia()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync();

        await CrearServicio().IntentarAsignarAsync(pedido.PedidoId);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync(a => a.PedidoId == pedido.PedidoId);
        asignacion.RepartidorId.Should().Be(RiderCercano);
        asignacion.Estado.Should().Be(AsignacionRepartidor.EstadoAsignado);
        asignacion.DistanciaMetros.Should().BeGreaterThan(900).And.BeLessThan(1100);
    }

    [Fact]
    public async Task PublicaElEventoPedidoAsignado()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync();

        await CrearServicio().IntentarAsignarAsync(pedido.PedidoId);

        _publish.Verify(
            p => p.Publish(
                It.Is<PedidoAsignado>(e => e.PedidoId == pedido.PedidoId && e.RepartidorId == RiderCercano),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IgnoraAlRepartidorQueYaTieneUnaEntregaActiva()
    {
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderMedio, 3);

        // El más cercano ya está en ruta con otro pedido.
        await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);

        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().IntentarAsignarAsync(pedido.PedidoId);

        resultado.Asignado.Should().BeTrue();
        resultado.RepartidorId.Should().Be(RiderMedio, "el cercano está ocupado");
    }

    [Fact]
    public async Task SinCandidatosEnElRadio_ElPedidoQuedaEnBuscando()
    {
        await UbicarAsync(RiderLejano, 12); // fuera del radio de 5 km
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio(radioKm: 5).IntentarAsignarAsync(pedido.PedidoId);

        resultado.Asignado.Should().BeFalse();

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.Estado.Should().Be(PedidoDisponible.EstadoBuscando);
        actualizado.RepartidorId.Should().BeNull();
        _publish.Verify(
            p => p.Publish(It.IsAny<PedidoAsignado>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RespetaElRadioConfigurado()
    {
        await UbicarAsync(RiderLejano, 12);
        var pedido = await CrearPedidoAsync();

        // Con el radio por defecto no hay nadie; con uno más amplio, sí.
        (await CrearServicio(radioKm: 5).IntentarAsignarAsync(pedido.PedidoId)).Asignado.Should().BeFalse();
        (await CrearServicio(radioKm: 20).IntentarAsignarAsync(pedido.PedidoId)).Asignado.Should().BeTrue();
    }

    [Fact]
    public async Task NoAsignaUnPedidoQueYaTieneRepartidor()
    {
        await UbicarAsync(RiderMedio, 3);
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RiderCercano);

        var resultado = await CrearServicio().IntentarAsignarAsync(pedido.PedidoId);

        resultado.Asignado.Should().BeFalse("un pedido tiene un solo domiciliario activo");

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.RepartidorId.Should().Be(RiderCercano);
    }

    [Fact]
    public async Task ReintentaLosPedidosQueQuedaronEnBuscando()
    {
        // El pedido nace sin nadie en el radio...
        var pedido = await CrearPedidoAsync();
        (await CrearServicio().IntentarAsignarAsync(pedido.PedidoId)).Asignado.Should().BeFalse();

        // ...y después aparece un repartidor.
        await UbicarAsync(RiderCercano, 1);

        var asignados = await CrearServicio().ReintentarPendientesAsync();

        asignados.Should().Be(1);
        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.Estado.Should().Be(PedidoDisponible.EstadoAsignado);
        actualizado.RepartidorId.Should().Be(RiderCercano);
    }

    // ------------------------------------------------------------------ rechazo

    [Fact]
    public async Task Rechazo_DevuelveElPedidoABuscandoYReasigna()
    {
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderMedio, 3);
        var pedido = await CrearPedidoAsync();

        var servicio = CrearServicio();
        (await servicio.IntentarAsignarAsync(pedido.PedidoId)).RepartidorId.Should().Be(RiderCercano);

        var resultado = await servicio.RechazarAsync(pedido.PedidoId, RiderCercano, "No puedo atenderlo");

        resultado.Asignado.Should().BeTrue();
        resultado.RepartidorId.Should().Be(RiderMedio, "se reasigna automáticamente al siguiente");

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.RepartidorId.Should().Be(RiderMedio);

        // El rechazo queda en el historial: el pedido conserva los dos intentos.
        var historial = await _db.AsignacionesRepartidor.Where(a => a.PedidoId == pedido.PedidoId).ToListAsync();
        historial.Should().HaveCount(2);
        historial.Single(a => a.RepartidorId == RiderCercano).Estado.Should().Be(AsignacionRepartidor.EstadoRechazado);
        historial.Single(a => a.RepartidorId == RiderCercano).MotivoRechazo.Should().Be("No puedo atenderlo");
        historial.Single(a => a.RepartidorId == RiderMedio).Estado.Should().Be(AsignacionRepartidor.EstadoAsignado);
    }

    [Fact]
    public async Task Rechazo_SoloPuedeHacerloElRepartidorAsignado()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync();
        var servicio = CrearServicio();
        await servicio.IntentarAsignarAsync(pedido.PedidoId);

        var resultado = await servicio.RechazarAsync(pedido.PedidoId, RiderMedio, "No es mi pedido");

        resultado.Asignado.Should().BeFalse();
        resultado.Mensaje.Should().Contain("no está asignado");

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.RepartidorId.Should().Be(RiderCercano, "el pedido no cambia de manos");
    }

    [Fact]
    public async Task Rechazo_NoSePuedeDespuesDeConfirmarEnLocal()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync();
        var servicio = CrearServicio();
        await servicio.IntentarAsignarAsync(pedido.PedidoId);

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.Estado = PedidoDisponible.EstadoConfirmadoEnLocal;
        await _db.SaveChangesAsync();

        var resultado = await servicio.RechazarAsync(pedido.PedidoId, RiderCercano, "Ya retiré el pedido");

        resultado.Asignado.Should().BeFalse("el rechazo solo vale antes de llegar al local");
    }

    // ------------------------------------------------------- consulta del repartidor

    [Fact]
    public async Task ObtenerAsignados_DevuelveSoloLosPedidosDeEseRepartidor()
    {
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderMedio, 3);

        var pedidoCercano = await CrearPedidoAsync();
        var pedidoMedio = await CrearPedidoAsync();
        var servicio = CrearServicio();
        await servicio.IntentarAsignarAsync(pedidoCercano.PedidoId);
        await servicio.IntentarAsignarAsync(pedidoMedio.PedidoId);

        var asignados = await servicio.ObtenerAsignadosAsync(RiderCercano);

        asignados.Should().HaveCount(1);
        asignados[0].PedidoId.Should().Be(pedidoCercano.PedidoId);
    }
}
