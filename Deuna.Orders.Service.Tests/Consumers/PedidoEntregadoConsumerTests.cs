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
/// TASK-305: cuando Delivery cierra la entrega, Orders cierra el pedido.
///
/// Orders es el dueño del estado del pedido: sin este consumer, el restaurante seguiría
/// viendo el pedido como recién creado después de que el cliente ya lo recibió.
/// </summary>
public class PedidoEntregadoConsumerTests : IDisposable
{
    private readonly OrdersDbContext _db;

    public PedidoEntregadoConsumerTests()
    {
        _db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase($"entregado-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Pedido> CrearPedidoAsync(string? estado = null, DateTime? fechaEntrega = null)
    {
        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            Estado = estado ?? EstadosPedido.Buscando,
            Subtotal = 30000m,
            CostoEnvio = 5000m,
            Total = 35000m,
            DireccionEntregaId = Guid.NewGuid(),
            TokenQrLocal = new string('a', 32),
            TokenQrEntrega = new string('b', 32),
            FechaEntrega = fechaEntrega
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

    private PedidoEntregadoConsumer CrearConsumer() =>
        new(_db, NullLogger<PedidoEntregadoConsumer>.Instance);

    [Fact]
    public async Task PedidoEntregado_CierraElPedidoYRegistraLaFecha()
    {
        var pedido = await CrearPedidoAsync();
        var fechaEntrega = DateTime.UtcNow;

        await CrearConsumer().Consume(Contexto(new PedidoEntregado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            RepartidorId: Guid.NewGuid(),
            FechaEntrega: fechaEntrega,
            OccurredAt: DateTime.UtcNow)));

        var guardado = await _db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        guardado.Estado.Should().Be(PedidoEntregadoConsumer.EstadoEntregado);
        guardado.FechaEntrega.Should().Be(fechaEntrega);
    }

    [Fact]
    public async Task UnReenvioDelMensaje_NoReescribeLaFechaDeEntrega()
    {
        var fechaOriginal = DateTime.UtcNow.AddMinutes(-10);
        var pedido = await CrearPedidoAsync(PedidoEntregadoConsumer.EstadoEntregado, fechaOriginal);

        await CrearConsumer().Consume(Contexto(new PedidoEntregado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            RepartidorId: Guid.NewGuid(),
            FechaEntrega: DateTime.UtcNow,
            OccurredAt: DateTime.UtcNow)));

        var guardado = await _db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        guardado.FechaEntrega.Should().Be(fechaOriginal, "el pedido ya estaba entregado");
    }

    [Fact]
    public async Task PedidoEntregado_CierraElIntentoDelDomiciliario()
    {
        // El historial queda completo: quién lo llevó y cuándo terminó (TASK-308).
        var pedido = await CrearPedidoAsync();
        var repartidorId = Guid.NewGuid();
        var fechaEntrega = DateTime.UtcNow;

        _db.AsignacionesReplicadas.Add(new AsignacionReplicada
        {
            PedidoId = pedido.Id,
            RepartidorId = repartidorId,
            FechaAsignacion = fechaEntrega.AddMinutes(-30),
            EstadoAsignacion = AsignacionReplicada.EstadoEnRuta
        });
        await _db.SaveChangesAsync();

        await CrearConsumer().Consume(Contexto(new PedidoEntregado(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            RepartidorId: repartidorId,
            FechaEntrega: fechaEntrega,
            OccurredAt: DateTime.UtcNow)));

        var asignacion = await _db.AsignacionesReplicadas.SingleAsync();
        asignacion.EstadoAsignacion.Should().Be(AsignacionReplicada.EstadoCompletado);
        asignacion.FechaEntrega.Should().Be(fechaEntrega);
    }

    [Fact]
    public async Task UnPedidoQueNoExiste_NoRompeElConsumo()
    {
        // El evento puede llegar antes de que la proyección esté lista o para un pedido de
        // otra base: se registra y se sigue, no se hace fallar el mensaje.
        var consumer = CrearConsumer();

        var consumir = async () => await consumer.Consume(Contexto(new PedidoEntregado(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-INEXISTENTE",
            RepartidorId: Guid.NewGuid(),
            FechaEntrega: DateTime.UtcNow,
            OccurredAt: DateTime.UtcNow)));

        await consumir.Should().NotThrowAsync();
        (await _db.Pedidos.CountAsync()).Should().Be(0);
    }
}
