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
/// TASK-306: Feedback replica los repartidores registrados en Identity.
///
/// La encuesta guarda a qué repartidor se califica (US-004.1); sin la réplica ese
/// identificador llega sin perfil detrás y el restaurante no puede saber a quién
/// corresponde la calificación.
/// </summary>
public class RepartidorReplicadoTests : IDisposable
{
    private static readonly Guid RepartidorId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly FeedbackDbContext _db;

    public RepartidorReplicadoTests()
    {
        var options = new DbContextOptionsBuilder<FeedbackDbContext>()
            .UseInMemoryDatabase($"feedback-repartidores-{Guid.NewGuid()}")
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

    private static RepartidorRegistrado Evento(string nombre = "Carlos Pérez") => new(
        RepartidorId: RepartidorId,
        NombreCompleto: nombre,
        DocumentoIdentidad: "1020304050",
        CiudadOperacion: "Bogotá",
        FotoPerfilUrl: null,
        OccurredAt: DateTime.UtcNow);

    [Fact]
    public async Task RepartidorRegistrado_ReplicaElRepartidor()
    {
        var consumer = new RepartidorRegistradoConsumer(_db, NullLogger<RepartidorRegistradoConsumer>.Instance);
        var evento = Evento();

        await consumer.Consume(Contexto(evento));

        var repartidor = await _db.RepartidoresReplicados.SingleAsync();
        repartidor.Id.Should().Be(evento.RepartidorId);
        repartidor.NombreCompleto.Should().Be(evento.NombreCompleto);
        repartidor.CiudadOperacion.Should().Be(evento.CiudadOperacion);
        repartidor.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task RepartidorRegistrado_DosVeces_NoDuplica()
    {
        var consumer = new RepartidorRegistradoConsumer(_db, NullLogger<RepartidorRegistradoConsumer>.Instance);
        var evento = Evento();

        await consumer.Consume(Contexto(evento));
        await consumer.Consume(Contexto(evento));

        (await _db.RepartidoresReplicados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RepartidorRegistrado_Reenvio_ActualizaElPerfil()
    {
        var consumer = new RepartidorRegistradoConsumer(_db, NullLogger<RepartidorRegistradoConsumer>.Instance);

        await consumer.Consume(Contexto(Evento("Carlos Pérez")));
        await consumer.Consume(Contexto(Evento("Carlos Pérez Gómez")));

        var repartidores = await _db.RepartidoresReplicados.ToListAsync();
        repartidores.Should().HaveCount(1);
        repartidores[0].NombreCompleto.Should().Be("Carlos Pérez Gómez");
    }
}
