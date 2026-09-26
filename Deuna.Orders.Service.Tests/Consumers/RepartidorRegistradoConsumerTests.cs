using Deuna.Orders.Service.Consumers;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Orders.Service.Tests.Consumers;

/// <summary>
/// Rebanada 2 de "Servicios finalizados": Orders replica al domiciliario que Delivery
/// asigna, para que el historial de servicios se resuelva con una consulta de un solo
/// servicio.
///
/// La asignación y sus tiempos ya vivían en <c>pedidos_asignados_repartidor</c>, pero esa
/// tabla guarda el <c>RepartidorId</c>, no quién es: el nombre del domiciliario —la columna
/// que muestra la pantalla— no existía en ninguna tabla de Orders y el evento
/// <c>PedidoAsignado</c> no lo trae. Sin esta réplica, la pantalla tendría que pedirle los
/// nombres a Delivery en cada lectura.
/// </summary>
public class RepartidorRegistradoConsumerTests
{
    private static readonly Guid RepartidorId = Guid.Parse("dddddddd-1111-4111-8111-111111111111");

    private static OrdersDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase($"repartidores-replicados-{Guid.NewGuid()}")
            .Options);

    private static RepartidorRegistrado CreateEvent(string nombre = "Carlos Pérez") => new(
        RepartidorId: RepartidorId,
        NombreCompleto: nombre,
        DocumentoIdentidad: "1020304050",
        CiudadOperacion: "Bogotá",
        FotoPerfilUrl: "https://cdn.deuna.test/rider.jpg",
        OccurredAt: DateTime.UtcNow);

    private static RepartidorRegistradoConsumer Consumer(OrdersDbContext db) =>
        new(db, NullLogger<RepartidorRegistradoConsumer>.Instance);

    /// <summary>Pedido en el estado en que lo deja el flujo antes de asignarlo.</summary>
    private static async Task<Pedido> CrearPedidoAsync(OrdersDbContext db)
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

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [Fact]
    public async Task Consume_RepartidorRegistrado_DejaElDomiciliarioEnLaReplica()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => Consumer(db));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act
            await harness.InputQueueSendEndpoint.Send(evento);

            // Assert
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            var domiciliario = await db.RepartidoresReplicados.SingleAsync();
            domiciliario.Id.Should().Be(evento.RepartidorId);
            domiciliario.NombreCompleto.Should().Be(evento.NombreCompleto);
            domiciliario.CiudadOperacion.Should().Be(evento.CiudadOperacion);
            domiciliario.Activo.Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task UnReenvioDelMismoEvento_NoDuplicaElDomiciliario()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => Consumer(db));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act - el mismo registro se entrega dos veces (redelivery)
            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.SelectAsync<RepartidorRegistrado>().Take(2).Count()).Should().Be(2);

            // Assert - una sola fila replicada
            var domiciliarios = await db.RepartidoresReplicados.ToListAsync();
            domiciliarios.Should().HaveCount(1);
            domiciliarios[0].Id.Should().Be(RepartidorId);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task UnReenvioConElPerfilActualizado_RefrescaSinDuplicar()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => Consumer(db));

        await harness.Start();
        try
        {
            await harness.InputQueueSendEndpoint.Send(CreateEvent("Carlos Pérez"));
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            // Act - reenvío con el perfil corregido en Identity
            await harness.InputQueueSendEndpoint.Send(CreateEvent("Carlos Pérez Gómez"));
            (await harness.Consumed.SelectAsync<RepartidorRegistrado>().Take(2).Count()).Should().Be(2);

            // Assert - se actualiza, no se duplica
            var domiciliarios = await db.RepartidoresReplicados.ToListAsync();
            domiciliarios.Should().HaveCount(1);
            domiciliarios[0].NombreCompleto.Should().Be("Carlos Pérez Gómez");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task LaAsignacionYElDomiciliario_SeCruzanAunqueLleguenEnCualquierOrden()
    {
        // Los dos eventos viajan por colas distintas, así que la asignación puede llegar
        // antes que el perfil del domiciliario. El cruce por RepartidorId tiene que
        // resolverse igual: es la consulta que hace la pantalla, dentro de Orders.
        var db = CreateDbContext();
        var pedido = await CrearPedidoAsync(db);

        var harness = new InMemoryTestHarness();
        harness.Consumer(() => Consumer(db));
        harness.Consumer(() => new PedidoAsignadoConsumer(db, NullLogger<PedidoAsignadoConsumer>.Instance));

        await harness.Start();
        try
        {
            // Act - primero la asignación, después el perfil del domiciliario
            await harness.InputQueueSendEndpoint.Send(new PedidoAsignado(
                PedidoId: pedido.Id,
                Codigo: pedido.Codigo,
                RepartidorId: RepartidorId,
                OccurredAt: DateTime.UtcNow));
            (await harness.Consumed.Any<PedidoAsignado>()).Should().BeTrue();

            await harness.InputQueueSendEndpoint.Send(CreateEvent("Carlos Pérez"));
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            // Assert - el domiciliario de la asignación tiene nombre, sin salir de Orders
            var domiciliario = await (
                from asignacion in db.AsignacionesReplicadas
                join repartidor in db.RepartidoresReplicados on asignacion.RepartidorId equals repartidor.Id
                where asignacion.PedidoId == pedido.Id
                select repartidor.NombreCompleto).SingleAsync();

            domiciliario.Should().Be("Carlos Pérez");
        }
        finally
        {
            await harness.Stop();
        }
    }
}

/// <summary>
/// Verifica que el consumer quede registrado en el bus de MassTransit del servicio y que un
/// <c>RepartidorRegistrado</c> publicado termine en <c>repartidores_replicados</c>.
///
/// Levanta la API real contra un PostGIS en contenedor, así que de paso aplica las
/// migraciones sobre el motor de producción: una tabla que existe solo en InMemory es una
/// tabla que no existe.
/// </summary>
public class RepartidorRegistradoConsumerRegistrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RepartidorRegistradoConsumerRegistrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishedRepartidorRegistrado_IsConsumedAndPersisted()
    {
        // Arrange
        var repartidorId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var evento = new RepartidorRegistrado(
            RepartidorId: repartidorId,
            NombreCompleto: "Ana Torres",
            DocumentoIdentidad: "99887766",
            CiudadOperacion: "Medellín",
            FotoPerfilUrl: null,
            OccurredAt: DateTime.UtcNow);

        // Act
        await publishEndpoint.Publish(evento);

        // Assert - el bus in-memory entrega en background: se sondea con timeout
        var deadline = DateTime.UtcNow.AddSeconds(15);
        RepartidorReplicado? domiciliario = null;

        while (DateTime.UtcNow < deadline)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            domiciliario = await pollDb.RepartidoresReplicados.FirstOrDefaultAsync(r => r.Id == repartidorId);
            if (domiciliario is not null) break;
            await Task.Delay(200);
        }

        domiciliario.Should().NotBeNull();
        domiciliario!.NombreCompleto.Should().Be("Ana Torres");
        domiciliario.CiudadOperacion.Should().Be("Medellín");
    }
}
