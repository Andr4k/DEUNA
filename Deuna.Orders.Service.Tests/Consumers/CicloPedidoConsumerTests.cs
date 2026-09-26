using Deuna.Orders.Service.Consumers;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Deuna.Orders.Service.Tests.Consumers;

/// <summary>
/// TASK-308: Orders sigue el ciclo del pedido, no solo su final.
///
/// Sin estos consumers el pedido se quedaba en el estado en que nació hasta que se
/// entregaba: el restaurante no veía que ya salió a buscarlo alguien, ni quién, ni cuándo
/// pasó por cada hito.
/// </summary>
public class CicloPedidoConsumerTests : IDisposable
{
    private static readonly Guid RepartidorId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid OtroRepartidorId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

    private readonly OrdersDbContext _db;

    public CicloPedidoConsumerTests()
    {
        _db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase($"ciclo-pedido-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Pedido> CrearPedidoAsync()
    {
        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            Estado = EstadosPedido.Buscando,
            Subtotal = 30000m,
            CostoEnvio = 5000m,
            Total = 35000m,
            DireccionEntregaId = Guid.NewGuid(),
            TokenQrLocal = new string('a', 32),
            TokenQrEntrega = new string('b', 32)
        };

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido;
    }

    private static ConsumeContext<T> Contexto<T>(T mensaje) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(mensaje);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private PedidoAsignadoConsumer Asignado() =>
        new(_db, NullLogger<PedidoAsignadoConsumer>.Instance);

    private PedidoActualizadoConsumer Actualizado() =>
        new(_db, NullLogger<PedidoActualizadoConsumer>.Instance);

    /// <summary>Asigna el pedido como lo hace Delivery y deja la fila en el historial.</summary>
    private async Task AsignarAsync(Pedido pedido, Guid repartidorId, DateTime? cuando = null)
    {
        await Asignado().Consume(Contexto(new PedidoAsignado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            RepartidorId: repartidorId,
            OccurredAt: cuando ?? DateTime.UtcNow)));
    }

    private async Task ActualizarAsync(Pedido pedido, string estadoAnterior, string estadoNuevo, string? motivo = null, DateTime? cuando = null)
    {
        await Actualizado().Consume(Contexto(new PedidoActualizado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            EstadoAnterior: estadoAnterior,
            EstadoNuevo: estadoNuevo,
            OccurredAt: cuando ?? DateTime.UtcNow,
            Motivo: motivo)));
    }

    // ------------------------------------------------------------------- asignación

    [Fact]
    public async Task PedidoAsignado_PasaElPedidoAAsignadoYRegistraElIntento()
    {
        var pedido = await CrearPedidoAsync();

        await AsignarAsync(pedido, RepartidorId);

        var guardado = await _db.Pedidos.SingleAsync();
        guardado.Estado.Should().Be(EstadosPedido.Asignado);

        var asignacion = await _db.AsignacionesReplicadas.SingleAsync();
        asignacion.PedidoId.Should().Be(pedido.Id);
        asignacion.RepartidorId.Should().Be(RepartidorId);
        asignacion.EstadoAsignacion.Should().Be(AsignacionReplicada.EstadoAsignado);
    }

    [Fact]
    public async Task UnReenvioDeLaAsignacion_NoDuplicaElIntento()
    {
        var pedido = await CrearPedidoAsync();
        var cuando = DateTime.UtcNow;

        await AsignarAsync(pedido, RepartidorId, cuando);
        await AsignarAsync(pedido, RepartidorId, cuando);

        (await _db.AsignacionesReplicadas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ElMismoDomiciliarioReasignadoDespues_SiEsOtroIntento()
    {
        // Puede volver a recibir el pedido tras un rechazo y un reintento: eso es un intento
        // nuevo, no un duplicado del mensaje.
        var pedido = await CrearPedidoAsync();

        await AsignarAsync(pedido, RepartidorId, DateTime.UtcNow.AddMinutes(-5));
        await AsignarAsync(pedido, RepartidorId, DateTime.UtcNow);

        (await _db.AsignacionesReplicadas.CountAsync()).Should().Be(2);
    }

    // ------------------------------------------------------------- transiciones

    [Fact]
    public async Task ConfirmadoEnLocal_RegistraLaLlegada()
    {
        var pedido = await CrearPedidoAsync();
        await AsignarAsync(pedido, RepartidorId);

        await ActualizarAsync(pedido, EstadosPedido.Asignado, EstadosPedido.ConfirmadoEnLocal);

        var guardado = await _db.Pedidos.SingleAsync();
        guardado.Estado.Should().Be(EstadosPedido.ConfirmadoEnLocal);

        var asignacion = await _db.AsignacionesReplicadas.SingleAsync();
        asignacion.EstadoAsignacion.Should().Be(AsignacionReplicada.EstadoEnLocal);
        asignacion.FechaLlegadaLocal.Should().NotBeNull();
    }

    [Fact]
    public async Task EnRuta_RegistraLaSalida()
    {
        var pedido = await CrearPedidoAsync();
        await AsignarAsync(pedido, RepartidorId);
        await ActualizarAsync(pedido, EstadosPedido.Asignado, EstadosPedido.ConfirmadoEnLocal);

        await ActualizarAsync(pedido, EstadosPedido.ConfirmadoEnLocal, EstadosPedido.EnRuta);

        (await _db.Pedidos.SingleAsync()).Estado.Should().Be(EstadosPedido.EnRuta);

        var asignacion = await _db.AsignacionesReplicadas.SingleAsync();
        asignacion.EstadoAsignacion.Should().Be(AsignacionReplicada.EstadoEnRuta);
        asignacion.FechaSalidaRuta.Should().NotBeNull();
    }

    [Fact]
    public async Task ElRechazo_DevuelveElPedidoABusquedaYCierraElIntentoConSuMotivo()
    {
        var pedido = await CrearPedidoAsync();
        await AsignarAsync(pedido, RepartidorId);

        await ActualizarAsync(
            pedido, EstadosPedido.Asignado, EstadosPedido.Buscando, motivo: "No puedo atenderlo ahora");

        (await _db.Pedidos.SingleAsync()).Estado.Should().Be(EstadosPedido.Buscando);

        var asignacion = await _db.AsignacionesReplicadas.SingleAsync();
        asignacion.EstadoAsignacion.Should().Be(AsignacionReplicada.EstadoRechazado);
        asignacion.MotivoRechazo.Should().Be("No puedo atenderlo ahora");
    }

    [Fact]
    public async Task UnRechazoQueLlegaTarde_NoPisaUnaAsignacionPosterior()
    {
        // El rechazo y la reasignación viajan por colas distintas: si el rechazo llega
        // después, aplicarlo dejaría el pedido en búsqueda estando ya asignado.
        var pedido = await CrearPedidoAsync();
        var momentoDelRechazo = DateTime.UtcNow;

        await AsignarAsync(pedido, RepartidorId, momentoDelRechazo.AddSeconds(-30));
        // La reasignación llegó primero, con un intento más nuevo que el rechazo.
        await AsignarAsync(pedido, OtroRepartidorId, momentoDelRechazo.AddSeconds(5));

        await ActualizarAsync(
            pedido, EstadosPedido.Asignado, EstadosPedido.Buscando, cuando: momentoDelRechazo);

        (await _db.Pedidos.SingleAsync()).Estado.Should().Be(
            EstadosPedido.Asignado, "el pedido ya tenía una asignación posterior al rechazo");
    }

    [Fact]
    public async Task UnReenvioNoReescribeLosHitosYaRegistrados()
    {
        var pedido = await CrearPedidoAsync();
        await AsignarAsync(pedido, RepartidorId);

        await ActualizarAsync(pedido, EstadosPedido.Asignado, EstadosPedido.EnRuta, cuando: DateTime.UtcNow.AddMinutes(-1));
        var primeraSalida = (await _db.AsignacionesReplicadas.SingleAsync()).FechaSalidaRuta;

        await ActualizarAsync(pedido, EstadosPedido.Asignado, EstadosPedido.EnRuta, cuando: DateTime.UtcNow);

        (await _db.AsignacionesReplicadas.SingleAsync()).FechaSalidaRuta.Should().Be(primeraSalida);
    }

    [Fact]
    public async Task UnPedidoQueNoExiste_NoRompeElConsumo()
    {
        var consumir = async () => await Asignado().Consume(Contexto(new PedidoAsignado(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-INEXISTENTE",
            RepartidorId: RepartidorId,
            OccurredAt: DateTime.UtcNow)));

        await consumir.Should().NotThrowAsync();
        (await _db.AsignacionesReplicadas.CountAsync()).Should().Be(0);
    }
}
