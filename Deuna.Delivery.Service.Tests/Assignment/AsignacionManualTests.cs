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
/// Asignación manual desde el panel de administración.
///
/// El operador elige al repartidor, y el sistema solo acepta la elección si ese
/// repartidor está dentro del radio del pedido y libre. No hay reglas nuevas ni
/// excepciones: la asignación manual es un desempate humano sobre la misma lista de
/// candidatos que ya calcula el matching automático.
///
/// Redis real (TestContainers) por la misma razón que en la asignación automática: el
/// radio se valida con GEORADIUS, así que con un doble en memoria se probaría el doble.
/// </summary>
[Collection(RedisCollection.Name)]
public class AsignacionManualTests : IDisposable
{
    private const double LatPedido = 4.6097100;
    private const double LonPedido = -74.0817500;

    private static readonly Guid RiderCercano = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid RiderMedio = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid RiderLejano = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid RiderDeOtroLado = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000009");

    private readonly IConnectionMultiplexer _redis;
    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public AsignacionManualTests(RedisContainerFixture redis)
    {
        _redis = redis.CrearConexion();
        _db = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"asignacion-manual-{Guid.NewGuid()}")
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

    /// <summary>Telemetría de un repartidor a cierta distancia al norte del pedido.</summary>
    private async Task UbicarAsync(Guid riderId, double kmAlNorte)
    {
        var latitud = LatPedido + (kmAlNorte / 111.32);
        await _store.ActualizarAsync(riderId, latitud, LonPedido, DateTime.UtcNow);
    }

    private async Task<PedidoDisponible> CrearPedidoAsync(
        string estado = PedidoDisponible.EstadoBuscando,
        Guid? repartidorId = null)
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

    // ------------------------------------------------------- asignación manual

    [Fact]
    public async Task AsignaAlRepartidorElegido_AunqueNoSeaElMasCercano()
    {
        // Es la diferencia con la asignación automática: el sistema elige por cercanía,
        // el operador puede tener una razón que el algoritmo no ve. Su elección manda,
        // siempre que el elegido sea un candidato válido.
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderMedio, 2);
        await UbicarAsync(RiderLejano, 4);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderLejano);

        resultado.Asignado.Should().BeTrue();
        resultado.RepartidorId.Should().Be(RiderLejano, "el operador lo eligió a él");

        var actualizado = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        actualizado.Estado.Should().Be(PedidoDisponible.EstadoAsignado);
        actualizado.RepartidorId.Should().Be(RiderLejano);
    }

    [Fact]
    public async Task RegistraLaAsignacionConLaDistanciaDelCandidato()
    {
        // La distancia no puede quedar nula: `ObtenerAsync` la devuelve null porque no
        // calcula cercanía, así que el elegido tiene que salir de la lista de candidatos.
        await UbicarAsync(RiderLejano, 3);
        var pedido = await CrearPedidoAsync();

        await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderLejano);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync(a => a.PedidoId == pedido.PedidoId);
        asignacion.RepartidorId.Should().Be(RiderLejano);
        asignacion.Estado.Should().Be(AsignacionRepartidor.EstadoAsignado);
        asignacion.DistanciaMetros.Should().NotBeNull("la distancia se pierde si se usa ObtenerAsync");
        asignacion.DistanciaMetros!.Value.Should().BeGreaterThan(2800).And.BeLessThan(3200);
    }

    [Fact]
    public async Task PublicaElMismoEventoQueLaAsignacionAutomatica()
    {
        // Órdenes no se entera de que hubo una mano humana: consume el mismo contrato.
        await UbicarAsync(RiderMedio, 2);
        var pedido = await CrearPedidoAsync();

        await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderMedio);

        _publish.Verify(
            p => p.Publish(
                It.Is<PedidoAsignado>(e => e.PedidoId == pedido.PedidoId && e.RepartidorId == RiderMedio),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RechazaSiElRepartidorElegidoEstaFueraDelRadio()
    {
        // Está en Redis y con telemetría vigente, pero a 20 km y el radio es 5.
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderDeOtroLado, 20);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderDeOtroLado);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.RepartidorNoEsCandidato);

        var sinTocar = await _db.PedidosDisponibles.SingleAsync(p => p.Id == pedido.Id);
        sinTocar.Estado.Should().Be(PedidoDisponible.EstadoBuscando);
        sinTocar.RepartidorId.Should().BeNull();
    }

    [Fact]
    public async Task RechazaSiElRepartidorElegidoNoTieneTelemetria()
    {
        // Nunca reportó posición: no está en Redis, así que no es candidato de nada.
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderDeOtroLado);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.RepartidorNoEsCandidato);
    }

    [Fact]
    public async Task RechazaSiElRepartidorElegidoYaTieneUnaEntregaActiva()
    {
        // Distinto del anterior a propósito: está en el radio, pero ocupado. El operador
        // necesita saber CUÁL de las dos cosas pasó para decidir qué hacer.
        await UbicarAsync(RiderCercano, 1);
        await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);
        var pedido = await CrearPedidoAsync();

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderCercano);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.RepartidorOcupado);
        _publish.Verify(
            p => p.Publish(It.IsAny<PedidoAsignado>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RechazaSiElPedidoYaNoEstaEnBuscando()
    {
        // Un pedido ya asignado no se reasigna: un pedido tiene un solo domiciliario activo.
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RiderMedio);

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderCercano);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.EstadoInvalido);
    }

    [Fact]
    public async Task RechazaUnPedidoQueNoEstaProyectadoEnDelivery()
    {
        await UbicarAsync(RiderCercano, 1);

        var resultado = await CrearServicio().AsignarManualmenteAsync(Guid.NewGuid(), RiderCercano);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.PedidoNoEncontrado);
    }

    [Fact]
    public async Task RechazaUnPedidoTerminalQueQuedoSinRepartidor()
    {
        // La guarda mira DOS cosas a la vez: el estado y el repartidor.
        //
        // Este caso es el que las distingue. Un pedido que terminó sin repartidor (cancelado
        // antes de que se lo asigne alguien) cumple "está sin repartidor", así que una
        // guarda debilitada a "estado inválido Y con repartidor" lo dejaría pasar y se
        // asignaría un pedido muerto.
        await UbicarAsync(RiderCercano, 1);
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoCancelado, repartidorId: null);

        var resultado = await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderCercano);

        resultado.Asignado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoAsignacion.EstadoInvalido);

        var asignaciones = await _db.AsignacionesRepartidor
            .Where(a => a.PedidoId == pedido.PedidoId)
            .ToListAsync();
        asignaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task UnPedidoCanceladoLiberaAlDomiciliario()
    {
        // El punto del consumer de PedidoCancelado. Si el pedido se cancela, el domiciliario
        // deja de estar ocupado y puede recibir otro; sin esto quedaba trabado para siempre,
        // porque un pedido cancelado seguía contando como entrega activa.
        //
        // Se prueba por el comportamiento público: la asignación que antes se rechaza, después
        // se acepta. El consumer se encarga de dejar el estado en 'Cancelado' (eso lo fija su
        // propia suite); acá se prueba que ese estado no ocupa al domiciliario.
        await UbicarAsync(RiderCercano, 1);
        var cancelado = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RiderCercano);
        var nuevo = await CrearPedidoAsync();

        var antes = await CrearServicio().AsignarManualmenteAsync(nuevo.PedidoId, RiderCercano);
        antes.Asignado.Should().BeFalse("todavía tiene una entrega activa");
        antes.Motivo.Should().Be(MotivoAsignacion.RepartidorOcupado);

        cancelado.Estado = PedidoDisponible.EstadoCancelado;
        await _db.SaveChangesAsync();

        var despues = await CrearServicio().AsignarManualmenteAsync(nuevo.PedidoId, RiderCercano);
        despues.Asignado.Should().BeTrue("el pedido cancelado ya no lo ocupa");
        despues.RepartidorId.Should().Be(RiderCercano);
    }

    [Fact]
    public async Task NoDejaAsignacionesParcialesCuandoRechaza()
    {
        // Nada a medias: si el elegido no sirve, no queda fila en el historial ni evento.
        await UbicarAsync(RiderCercano, 1);
        await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);
        var pedido = await CrearPedidoAsync();

        await CrearServicio().AsignarManualmenteAsync(pedido.PedidoId, RiderCercano);

        var asignaciones = await _db.AsignacionesRepartidor
            .Where(a => a.PedidoId == pedido.PedidoId)
            .ToListAsync();
        asignaciones.Should().BeEmpty();
        _publish.Verify(
            p => p.Publish(It.IsAny<PedidoAsignado>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
