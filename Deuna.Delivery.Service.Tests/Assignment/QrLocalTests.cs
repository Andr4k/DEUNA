using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Deuna.Delivery.Service.Tests.Assignment;

/// <summary>
/// TASK-304: validación del QR de recogida en el local.
///
/// El domiciliario escanea el QR que muestra el restaurante y eso confirma que llegó
/// físicamente. No hace falta Redis: la validación solo compara contra el token replicado
/// y mueve el estado del pedido.
/// </summary>
public class QrLocalTests : IDisposable
{
    private static readonly Guid RepartidorId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OtroRepartidorId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private const string TokenLocal = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
    private const string TokenEntrega = "ffffffffffffffffffffffffffffffff";

    private readonly DeliveryDbContext _db;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public QrLocalTests()
    {
        _db = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"qr-local-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private IAsignacionService CrearServicio() =>
        new AsignacionService(
            _db,
            new TrackingStoreNulo(),
            _publish.Object,
            Options.Create(new AsignacionOptions()),
            NullLogger<AsignacionService>.Instance);

    /// <summary>Pedido tal como lo deja la asignación automática: asignado y con su historial.</summary>
    private async Task<PedidoDisponible> CrearPedidoAsignadoAsync(Guid? repartidorId = null)
    {
        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 35000m,
            Estado = repartidorId is null ? PedidoDisponible.EstadoBuscando : PedidoDisponible.EstadoAsignado,
            TokenQrLocal = TokenLocal,
            TokenQrEntrega = TokenEntrega,
            RepartidorId = repartidorId,
            AsignadoAt = repartidorId is null ? null : DateTime.UtcNow
        };

        _db.PedidosDisponibles.Add(pedido);

        if (repartidorId is not null)
        {
            _db.AsignacionesRepartidor.Add(new AsignacionRepartidor
            {
                PedidoId = pedido.PedidoId,
                RepartidorId = repartidorId.Value,
                Estado = AsignacionRepartidor.EstadoAsignado,
                DistanciaMetros = 450
            });
        }

        await _db.SaveChangesAsync();
        return pedido;
    }

    // ------------------------------------------------------------------ camino feliz

    [Fact]
    public async Task TokenValido_ConfirmaLaLlegadaYHabilitaLaEntrega()
    {
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        var resultado = await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenLocal);

        resultado.Valido.Should().BeTrue();
        resultado.Estado.Should().Be(PedidoDisponible.EstadoConfirmadoEnLocal);
        resultado.FechaLlegadaLocal.Should().NotBeNull();

        var guardado = await _db.PedidosDisponibles.SingleAsync(p => p.PedidoId == pedido.PedidoId);
        guardado.Estado.Should().Be(PedidoDisponible.EstadoConfirmadoEnLocal);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync();
        asignacion.Estado.Should().Be(AsignacionRepartidor.EstadoConfirmadoEnLocal);
        asignacion.FechaLlegadaLocal.Should().NotBeNull("es la prueba de que llegó físicamente");
    }

    [Fact]
    public async Task ElTokenEscaneadoConEspacios_SeAcepta()
    {
        // Un lector de QR puede devolver el valor con espacios o saltos de línea.
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        var resultado = await CrearServicio().ValidarQrLocalAsync(
            pedido.PedidoId, RepartidorId, $"  {TokenLocal}\n");

        resultado.Valido.Should().BeTrue();
    }

    [Fact]
    public async Task ValidarElQrLocal_PublicaLaTransicionDeEstado()
    {
        // La llegada al local es una transición de la máquina de estados: si no se publica,
        // los demás servicios siguen viendo el pedido como recién asignado (TASK-308).
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenLocal);

        _publish.Verify(p => p.Publish(
            It.Is<PedidoActualizado>(e =>
                e.PedidoId == pedido.PedidoId &&
                e.EstadoAnterior == PedidoDisponible.EstadoAsignado &&
                e.EstadoNuevo == PedidoDisponible.EstadoConfirmadoEnLocal),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnTokenQueNoCorresponde_NoPublicaNada()
    {
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, new string('0', 32));

        _publish.Verify(
            p => p.Publish(It.IsAny<PedidoActualizado>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------- rechazos

    [Fact]
    public async Task UnTokenQueNoCorresponde_RechazaSinTocarElEstado()
    {
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        var resultado = await CrearServicio().ValidarQrLocalAsync(
            pedido.PedidoId, RepartidorId, "00000000000000000000000000000000");

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.TokenInvalido);

        var guardado = await _db.PedidosDisponibles.SingleAsync(p => p.PedidoId == pedido.PedidoId);
        guardado.Estado.Should().Be(PedidoDisponible.EstadoAsignado);
    }

    [Fact]
    public async Task ElTokenDeEntrega_NoSirveComoTokenDelLocal()
    {
        // Son de partes distintas: si sirvieran indistintamente, el QR del local no probaría
        // nada (FR-002.5).
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        var resultado = await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenEntrega);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.TokenInvalido);
    }

    [Fact]
    public async Task OtroDomiciliario_NoPuedeValidarElPedido()
    {
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);

        var resultado = await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, OtroRepartidorId, TokenLocal);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.NoAsignado);
    }

    [Fact]
    public async Task UnPedidoSinAsignar_NoSePuedeValidar()
    {
        var pedido = await CrearPedidoAsignadoAsync(repartidorId: null);

        var resultado = await CrearServicio().ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenLocal);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.NoAsignado);
    }

    [Fact]
    public async Task UnPedidoQueNoEstaProyectado_DevuelveNoEncontrado()
    {
        var resultado = await CrearServicio().ValidarQrLocalAsync(Guid.NewGuid(), RepartidorId, TokenLocal);

        resultado.Valido.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.PedidoNoEncontrado);
    }

    [Fact]
    public async Task LaSegundaValidacion_NoVuelveAMoverLaLlegada()
    {
        var pedido = await CrearPedidoAsignadoAsync(RepartidorId);
        var servicio = CrearServicio();

        var primera = await servicio.ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenLocal);
        var llegada = primera.FechaLlegadaLocal;

        var segunda = await servicio.ValidarQrLocalAsync(pedido.PedidoId, RepartidorId, TokenLocal);

        segunda.Valido.Should().BeFalse("la llegada ya estaba confirmada");
        segunda.Motivo.Should().Be(MotivoRechazo.EstadoInvalido);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync();
        asignacion.FechaLlegadaLocal.Should().Be(llegada, "la hora de llegada no se reescribe");
    }
}
