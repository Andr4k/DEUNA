using Deuna.Delivery.Service.Consumers;
using Deuna.Delivery.Service.Events;
using Deuna.Delivery.Service.Models;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Delivery.Service.Tests.Consumers;

/// <summary>
/// TASK-301: el consumer de PedidoCreado proyecta el pedido en pedidos_disponibles
/// con estado "Buscando", de forma idempotente y con trazabilidad (CorrelationId).
/// </summary>
public class PedidoCreadoConsumerTests
{
    private static readonly Guid PedidoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClienteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RestauranteId = Guid.Parse("a1b2c3d4-5e6f-4a8b-9c0d-1e2f3a4b5c6d");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static DeliveryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"pedidos-disponibles-{Guid.NewGuid()}")
            .Options);

    private static PedidoCreado CreateEvent() => new(
        PedidoId: PedidoId,
        Codigo: "PED-00042",
        ClienteId: ClienteId,
        RestauranteId: RestauranteId,
        Total: 62000m,
        Estado: "Pendiente",
        QrCodigo: "qr-token-abc123",
        OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task Consume_PedidoCreado_ProjectsPedidoAsBuscando()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new PedidoCreadoConsumer(db, NullLogger<PedidoCreadoConsumer>.Instance));

        await harness.Start();
        try
        {
            // Act
            await harness.InputQueueSendEndpoint.Send(CreateEvent());

            // Assert
            (await harness.Consumed.Any<PedidoCreado>()).Should().BeTrue();

            var pedido = await db.PedidosDisponibles.SingleAsync();
            pedido.PedidoId.Should().Be(PedidoId);
            pedido.Estado.Should().Be(PedidoDisponible.EstadoBuscando);
            pedido.RepartidorId.Should().BeNull();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_PedidoCreado_MapsAllEventFields()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new PedidoCreadoConsumer(db, NullLogger<PedidoCreadoConsumer>.Instance));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act
            await harness.InputQueueSendEndpoint.Send(evento, context => context.CorrelationId = CorrelationId);

            (await harness.Consumed.Any<PedidoCreado>()).Should().BeTrue();

            // Assert
            var pedido = await db.PedidosDisponibles.SingleAsync();
            pedido.Codigo.Should().Be(evento.Codigo);
            pedido.ClienteId.Should().Be(evento.ClienteId);
            pedido.RestauranteId.Should().Be(evento.RestauranteId);
            pedido.Total.Should().Be(evento.Total);
            pedido.QrCodigo.Should().Be(evento.QrCodigo);
            pedido.CorrelationId.Should().Be(CorrelationId);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_PedidoCreado_Twice_IsIdempotent()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new PedidoCreadoConsumer(db, NullLogger<PedidoCreadoConsumer>.Instance));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act - el mismo pedido se entrega dos veces (redelivery)
            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.Any<PedidoCreado>()).Should().BeTrue();

            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.SelectAsync<PedidoCreado>().Take(2).Count()).Should().Be(2);

            // Assert - una sola fila proyectada
            var pedidos = await db.PedidosDisponibles.ToListAsync();
            pedidos.Should().HaveCount(1);
            pedidos[0].PedidoId.Should().Be(PedidoId);
        }
        finally
        {
            await harness.Stop();
        }
    }
}

/// <summary>
/// Verifica que el consumer quede registrado en el bus de MassTransit del servicio
/// y que un PedidoCreado publicado termine en pedidos_disponibles.
/// </summary>
public class PedidoCreadoConsumerRegistrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PedidoCreadoConsumerRegistrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishedPedidoCreado_IsConsumedAndPersisted()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        var evento = new PedidoCreado(
            PedidoId: pedidoId,
            Codigo: "PED-90001",
            ClienteId: Guid.NewGuid(),
            RestauranteId: Guid.NewGuid(),
            Total: 45000m,
            Estado: "Pendiente",
            QrCodigo: "qr-registration-token",
            OccurredAt: DateTime.UtcNow);

        // Act
        await publishEndpoint.Publish(evento);

        // Assert - el bus in-memory entrega en background: se sondea con timeout
        var deadline = DateTime.UtcNow.AddSeconds(10);
        PedidoDisponible? pedido = null;

        while (DateTime.UtcNow < deadline)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            pedido = await pollDb.PedidosDisponibles.FirstOrDefaultAsync(p => p.PedidoId == pedidoId);
            if (pedido is not null) break;
            await Task.Delay(200);
        }

        pedido.Should().NotBeNull();
        pedido!.Estado.Should().Be(PedidoDisponible.EstadoBuscando);
        pedido.Codigo.Should().Be("PED-90001");
    }
}
