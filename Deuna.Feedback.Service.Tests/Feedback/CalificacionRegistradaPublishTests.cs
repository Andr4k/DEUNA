using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Deuna.Feedback.Service.Services;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Deuna.Feedback.Service.Tests.Feedback;

/// <summary>
/// Rebanada 3 de "servicios finalizados": Feedback publica la calificación registrada.
///
/// La encuesta guarda dos notas de sujetos distintos —el restaurante en
/// <c>RatingGeneralComida</c> y el domiciliario en <c>RatingServicioRepartidor</c>—. El
/// test las usa distintas a propósito: si el evento las cruza, Orders guarda la nota del
/// domiciliario como si fuera la del restaurante, que es peor que dejarlas en null.
/// </summary>
public class CalificacionRegistradaPublishTests : IDisposable
{
    private readonly FeedbackDbContext _db;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public CalificacionRegistradaPublishTests()
    {
        _db = new FeedbackDbContext(new DbContextOptionsBuilder<FeedbackDbContext>()
            .UseInMemoryDatabase($"calificacion-publish-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private FeedbackService Servicio() => new(
        _db,
        Options.Create(new FeedbackOptions()),
        NullLogger<FeedbackService>.Instance,
        _publish.Object);

    private async Task<PedidoReplicado> SembrarPedidoAsync(
        string estado = PedidoReplicado.EstadoEntregado, Guid? repartidorId = null)
    {
        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            RepartidorId = repartidorId,
            Estado = estado,
            Total = 55000m
        };

        _db.PedidosReplicados.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido;
    }

    [Fact]
    public async Task RegistrarElNivel1_PublicaLaCalificacionConCadaNotaEnSuSujeto()
    {
        var repartidorId = Guid.NewGuid();
        var pedido = await SembrarPedidoAsync(repartidorId: repartidorId);

        var resultado = await Servicio().EnviarFeedbackBasicoAsync(
            new SubmitBasicFeedbackRequest(pedido.PedidoId, 5, 4, true));

        resultado.Resultado.Should().Be(ResultadoFeedback.Ok);

        _publish.Verify(p => p.Publish(
            It.Is<CalificacionRegistrada>(e =>
                e.PedidoId == pedido.PedidoId
                && e.Codigo == pedido.Codigo
                && e.RepartidorId == repartidorId
                // El restaurante con la nota de comida y el domiciliario con la del repartidor.
                && e.CalificacionRestaurante == 5
                && e.CalificacionDomiciliario == 4),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UnFeedbackRechazado_NoPublicaNada()
    {
        // El evento se publica después de persistir: anunciar una calificación que no se
        // guardó dejaría a Feedback y a Orders contando historias distintas.
        var pedido = await SembrarPedidoAsync(estado: "EnRuta");

        var resultado = await Servicio().EnviarFeedbackBasicoAsync(
            new SubmitBasicFeedbackRequest(pedido.PedidoId, 5, 4, true));

        resultado.Resultado.Should().Be(ResultadoFeedback.PedidoNoEntregado);

        _publish.Verify(
            p => p.Publish(It.IsAny<CalificacionRegistrada>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
