using Deuna.Notification.Service.Consumers;
using Deuna.Notification.Service.Models;
using Deuna.Notification.Service.Services;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Deuna.Notification.Service.Tests;

/// <summary>
/// Los consumers traducen eventos de dominio en notificaciones: a quién y con qué texto.
/// Esa traducción es la responsabilidad del servicio; el envío real queda detrás de IPushSender.
/// </summary>
public class NotificacionConsumersTests : IDisposable
{
    private static readonly Guid RiderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RestauranteId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly NotificationDbContext _db;
    private readonly ProveedorFalso _proveedor = new();

    public NotificacionConsumersTests()
    {
        _db = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"consumers-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private INotificacionService Servicio() =>
        new NotificacionService(_db, _proveedor, NullLogger<NotificacionService>.Instance);

    private static ConsumeContext<T> Contexto<T>(T mensaje) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(mensaje);
        context.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task PedidoAsignado_NotificaAlDomiciliario()
    {
        await Servicio().RegistrarDispositivoAsync(RiderId, "token-rider", "android");
        var consumer = new PedidoAsignadoConsumer(Servicio(), NullLogger<PedidoAsignadoConsumer>.Instance);

        await consumer.Consume(Contexto(new PedidoAsignado(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-20260923-000012",
            RepartidorId: RiderId,
            OccurredAt: DateTime.UtcNow)));

        _proveedor.Envios.Should().HaveCount(1);
        _proveedor.Envios[0].Token.Should().Be("token-rider");
        _proveedor.Envios[0].Mensaje.Titulo.Should().Contain("asignado");
        _proveedor.Envios[0].Mensaje.Cuerpo.Should().Contain("PED-20260923-000012");
        _proveedor.Envios[0].Mensaje.Datos["tipo"].Should().Be("PedidoAsignado");

        var registro = await _db.NotificacionesEnviadas.SingleAsync();
        registro.DestinatarioId.Should().Be(RiderId);
        registro.Estado.Should().Be(NotificacionEnviada.EstadoEnviada);
    }

    [Fact]
    public async Task PedidoCreado_NotificaAlRestaurante()
    {
        await Servicio().RegistrarDispositivoAsync(RestauranteId, "token-resto", "android");
        var consumer = new PedidoCreadoConsumer(Servicio(), NullLogger<PedidoCreadoConsumer>.Instance);

        await consumer.Consume(Contexto(new PedidoCreado(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-20260923-000013",
            ClienteId: Guid.NewGuid(),
            RestauranteId: RestauranteId,
            Total: 55000m,
            Estado: "Pendiente",
            TokenQrLocal: "qr-local-abc",
            TokenQrEntrega: "qr-entrega-xyz",
            OccurredAt: DateTime.UtcNow)));

        _proveedor.Envios.Should().HaveCount(1);
        _proveedor.Envios[0].Token.Should().Be("token-resto");
        _proveedor.Envios[0].Mensaje.Cuerpo.Should().Contain("PED-20260923-000013");
        _proveedor.Envios[0].Mensaje.Datos["tipo"].Should().Be("PedidoCreado");
    }

    [Fact]
    public async Task UnDestinatarioSinDispositivo_NoRompeElConsumo()
    {
        // El domiciliario no tiene la app instalada (o nunca registró su token).
        var consumer = new PedidoAsignadoConsumer(Servicio(), NullLogger<PedidoAsignadoConsumer>.Instance);

        await consumer.Consume(Contexto(new PedidoAsignado(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-1",
            RepartidorId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow)));

        _proveedor.Envios.Should().BeEmpty();
        var registro = await _db.NotificacionesEnviadas.SingleAsync();
        registro.Estado.Should().Be(NotificacionEnviada.EstadoSinDispositivo);
    }

    private sealed class ProveedorFalso : IPushSender
    {
        public List<(string Token, MensajePush Mensaje)> Envios { get; } = [];

        public Task<ResultadoPush> EnviarAsync(string token, MensajePush mensaje, CancellationToken cancellationToken = default)
        {
            Envios.Add((token, mensaje));
            return Task.FromResult(new ResultadoPush(true, "falso"));
        }
    }
}
