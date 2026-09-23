using Deuna.Feedback.Service.Consumers;
using Deuna.Feedback.Service.Models;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Deuna.Feedback.Service.Tests.Consumers;

/// <summary>
/// La proyección local de pedidos alimenta la validación del feedback:
/// sin ella no se puede saber si el pedido existe y está Entregado.
/// </summary>
public class PedidoProjectionTests : IDisposable
{
    private readonly FeedbackDbContext _db;

    public PedidoProjectionTests()
    {
        var options = new DbContextOptionsBuilder<FeedbackDbContext>()
            .UseInMemoryDatabase($"feedback-projection-{Guid.NewGuid()}")
            .Options;
        _db = new FeedbackDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<T> Contexto<T>(T mensaje) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(mensaje);
        context.SetupGet(c => c.CorrelationId).Returns(Guid.NewGuid());
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static PedidoCreado EventoPedidoCreado(Guid pedidoId) => new(
        PedidoId: pedidoId,
        Codigo: "PED-20260923-000001",
        ClienteId: Guid.NewGuid(),
        RestauranteId: Guid.NewGuid(),
        Total: 55000m,
        Estado: "Pendiente",
        TokenQrLocal: Guid.NewGuid().ToString("N"),
        TokenQrEntrega: Guid.NewGuid().ToString("N"),
        OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task PedidoCreado_ProyectaElPedidoConSuEstadoInicial()
    {
        var pedidoId = Guid.NewGuid();
        var consumer = new PedidoCreadoConsumer(_db, NullLogger<PedidoCreadoConsumer>.Instance);

        await consumer.Consume(Contexto(EventoPedidoCreado(pedidoId)));

        var pedido = await _db.PedidosReplicados.SingleAsync(p => p.PedidoId == pedidoId);
        pedido.Estado.Should().Be("Pendiente");
        pedido.Codigo.Should().Be("PED-20260923-000001");
        pedido.RestauranteId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PedidoCreado_Duplicado_NoDuplicaElRegistro()
    {
        var pedidoId = Guid.NewGuid();
        var consumer = new PedidoCreadoConsumer(_db, NullLogger<PedidoCreadoConsumer>.Instance);
        var evento = EventoPedidoCreado(pedidoId);

        await consumer.Consume(Contexto(evento));
        await consumer.Consume(Contexto(evento));

        (await _db.PedidosReplicados.CountAsync(p => p.PedidoId == pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task PedidoAsignado_RegistraElRepartidor()
    {
        var pedidoId = Guid.NewGuid();
        var repartidorId = Guid.NewGuid();
        await new PedidoCreadoConsumer(_db, NullLogger<PedidoCreadoConsumer>.Instance)
            .Consume(Contexto(EventoPedidoCreado(pedidoId)));

        var consumer = new PedidoAsignadoConsumer(_db, NullLogger<PedidoAsignadoConsumer>.Instance);
        await consumer.Consume(Contexto(new PedidoAsignado(pedidoId, "PED-20260923-000001", repartidorId, DateTime.UtcNow)));

        var pedido = await _db.PedidosReplicados.SingleAsync(p => p.PedidoId == pedidoId);
        pedido.RepartidorId.Should().Be(repartidorId);
    }

    [Fact]
    public async Task PedidoActualizado_ActualizaElEstado()
    {
        var pedidoId = Guid.NewGuid();
        await new PedidoCreadoConsumer(_db, NullLogger<PedidoCreadoConsumer>.Instance)
            .Consume(Contexto(EventoPedidoCreado(pedidoId)));

        var consumer = new PedidoActualizadoConsumer(_db, NullLogger<PedidoActualizadoConsumer>.Instance);
        await consumer.Consume(Contexto(new PedidoActualizado(
            pedidoId, "PED-20260923-000001", "EnRuta", PedidoReplicado.EstadoEntregado, DateTime.UtcNow)));

        var pedido = await _db.PedidosReplicados.SingleAsync(p => p.PedidoId == pedidoId);
        pedido.Estado.Should().Be(PedidoReplicado.EstadoEntregado);
        pedido.ActualizadoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task PedidoActualizado_DePedidoDesconocido_NoFalla()
    {
        var consumer = new PedidoActualizadoConsumer(_db, NullLogger<PedidoActualizadoConsumer>.Instance);

        await consumer.Consume(Contexto(new PedidoActualizado(
            Guid.NewGuid(), "PED-INEXISTENTE", "EnRuta", PedidoReplicado.EstadoEntregado, DateTime.UtcNow)));

        (await _db.PedidosReplicados.CountAsync()).Should().Be(0);
    }
}
