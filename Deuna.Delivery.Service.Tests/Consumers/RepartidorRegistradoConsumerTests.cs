using Deuna.Delivery.Service.Consumers;
using Deuna.Delivery.Service.Models;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Delivery.Service.Tests.Consumers;

/// <summary>
/// TASK-306: Delivery replica los repartidores registrados en Identity.
///
/// Sin esta réplica la asignación automática no tiene con qué trabajar: no sabe qué
/// domiciliarios existen ni cuáles están disponibles, así que el matching por cercanía
/// solo puede devolver identificadores sin perfil detrás.
/// </summary>
public class RepartidorRegistradoConsumerTests
{
    private static readonly Guid RepartidorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static DeliveryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"repartidores-{Guid.NewGuid()}")
            .Options);

    private static RepartidorRegistrado CreateEvent(string nombre = "Carlos Pérez") => new(
        RepartidorId: RepartidorId,
        NombreCompleto: nombre,
        DocumentoIdentidad: "1020304050",
        CiudadOperacion: "Bogotá",
        FotoPerfilUrl: "https://cdn.deuna.test/rider.jpg",
        OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task Consume_RepartidorRegistrado_ReplicaElRepartidor()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new RepartidorRegistradoConsumer(db, NullLogger<RepartidorRegistradoConsumer>.Instance));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act
            await harness.InputQueueSendEndpoint.Send(evento);

            // Assert
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            var repartidor = await db.RepartidoresReplicados.SingleAsync();
            repartidor.Id.Should().Be(evento.RepartidorId);
            repartidor.NombreCompleto.Should().Be(evento.NombreCompleto);
            repartidor.DocumentoIdentidad.Should().Be(evento.DocumentoIdentidad);
            repartidor.CiudadOperacion.Should().Be(evento.CiudadOperacion);
            repartidor.FotoPerfilUrl.Should().Be(evento.FotoPerfilUrl);
            repartidor.Activo.Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_RepartidorRegistrado_Twice_IsIdempotent()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new RepartidorRegistradoConsumer(db, NullLogger<RepartidorRegistradoConsumer>.Instance));

        var evento = CreateEvent();

        await harness.Start();
        try
        {
            // Act - el mismo repartidor se entrega dos veces (redelivery)
            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.SelectAsync<RepartidorRegistrado>().Take(2).Count()).Should().Be(2);

            // Assert - una sola fila replicada
            var repartidores = await db.RepartidoresReplicados.ToListAsync();
            repartidores.Should().HaveCount(1);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_RepartidorRegistrado_ActualizaElPerfilEnElReenvio()
    {
        // Arrange
        var db = CreateDbContext();
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new RepartidorRegistradoConsumer(db, NullLogger<RepartidorRegistradoConsumer>.Instance));

        await harness.Start();
        try
        {
            await harness.InputQueueSendEndpoint.Send(CreateEvent("Carlos Pérez"));
            (await harness.Consumed.Any<RepartidorRegistrado>()).Should().BeTrue();

            // Act - reenvío con el perfil actualizado
            await harness.InputQueueSendEndpoint.Send(CreateEvent("Carlos Pérez Gómez"));
            (await harness.Consumed.SelectAsync<RepartidorRegistrado>().Take(2).Count()).Should().Be(2);

            // Assert - se actualiza, no se duplica
            var repartidores = await db.RepartidoresReplicados.ToListAsync();
            repartidores.Should().HaveCount(1);
            repartidores[0].NombreCompleto.Should().Be("Carlos Pérez Gómez");
        }
        finally
        {
            await harness.Stop();
        }
    }
}

/// <summary>
/// Verifica que el consumer quede registrado en el bus del servicio: sin el registro
/// en Program.cs el tipo compila y los tests unitarios pasan, pero ningún mensaje
/// llega nunca al consumer.
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
        var deadline = DateTime.UtcNow.AddSeconds(10);
        RepartidorReplicado? repartidor = null;

        while (DateTime.UtcNow < deadline)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
            repartidor = await pollDb.RepartidoresReplicados.FirstOrDefaultAsync(r => r.Id == repartidorId);
            if (repartidor is not null) break;
            await Task.Delay(200);
        }

        repartidor.Should().NotBeNull();
        repartidor!.NombreCompleto.Should().Be("Ana Torres");
    }
}
