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
/// Rebanada 3 de "servicios finalizados": la calificación del pedido entra a la réplica.
///
/// La pantalla muestra dos notas por fila —la del domiciliario y la del restaurante— y
/// Orders no puede leerlas de Feedback sin acoplarse a otro servicio en una lectura. Sin
/// este consumer las dos columnas salen en null.
///
/// El riesgo que estos tests cubren no es que falte la fila, sino que las notas queden
/// cruzadas: un domiciliario calificado con la nota del restaurante es peor que un null.
/// Por eso los dos puntajes son distintos en cada caso.
/// </summary>
public class CalificacionReplicadaConsumerTests : IDisposable
{
    private static readonly Guid RepartidorId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private readonly OrdersDbContext _db;

    public CalificacionReplicadaConsumerTests()
    {
        _db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase($"calificacion-replicada-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Pedido> CrearPedidoEntregadoAsync()
    {
        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            Estado = EstadosPedido.Entregado,
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

    private CalificacionRegistradaConsumer Consumidor() =>
        new(_db, NullLogger<CalificacionRegistradaConsumer>.Instance);

    private Task CalificarAsync(
        Pedido pedido,
        int calificacionDomiciliario,
        int calificacionRestaurante,
        Guid? repartidorId = null,
        DateTime? cuando = null) =>
        Consumidor().Consume(Contexto(new CalificacionRegistrada(
            PedidoId: pedido.Id,
            Codigo: pedido.Codigo,
            RepartidorId: repartidorId,
            CalificacionRestaurante: calificacionRestaurante,
            CalificacionDomiciliario: calificacionDomiciliario,
            OccurredAt: cuando ?? DateTime.UtcNow)));

    [Fact]
    public async Task LaCalificacionConsumida_DejaLasDosNotasEnLaReplica()
    {
        var pedido = await CrearPedidoEntregadoAsync();

        await CalificarAsync(
            pedido, calificacionDomiciliario: 4, calificacionRestaurante: 5, repartidorId: RepartidorId);

        var guardada = await _db.CalificacionesReplicadas.SingleAsync();
        guardada.PedidoId.Should().Be(pedido.Id);
        guardada.RepartidorId.Should().Be(RepartidorId);

        // Cada nota en su sujeto: el domiciliario con la suya y el restaurante con la suya.
        guardada.CalificacionDomiciliario.Should().Be(4);
        guardada.CalificacionRestaurante.Should().Be(5);
    }

    [Fact]
    public async Task UnReenvioDeLaCalificacion_NoDuplicaLaFila()
    {
        var pedido = await CrearPedidoEntregadoAsync();
        var cuando = DateTime.UtcNow;

        await CalificarAsync(pedido, 4, 5, cuando: cuando);
        await CalificarAsync(pedido, 4, 5, cuando: cuando);

        (await _db.CalificacionesReplicadas.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UnaCalificacionSinDomiciliarioConocido_IgualGuardaLasDosNotas()
    {
        // La encuesta no exige que la asignación haya sido proyectada: el cliente califica
        // el servicio aunque no se sepa quién lo entregó, y la nota del restaurante no
        // depende de eso.
        var pedido = await CrearPedidoEntregadoAsync();

        await CalificarAsync(pedido, calificacionDomiciliario: 3, calificacionRestaurante: 5);

        var guardada = await _db.CalificacionesReplicadas.SingleAsync();
        guardada.RepartidorId.Should().BeNull();
        guardada.CalificacionDomiciliario.Should().Be(3);
        guardada.CalificacionRestaurante.Should().Be(5);
    }

    [Fact]
    public async Task UnPedidoQueNoExiste_NoRompeElConsumo()
    {
        var consumir = async () => await Consumidor().Consume(Contexto(new CalificacionRegistrada(
            PedidoId: Guid.NewGuid(),
            Codigo: "PED-INEXISTENTE",
            RepartidorId: RepartidorId,
            CalificacionRestaurante: 5,
            CalificacionDomiciliario: 4,
            OccurredAt: DateTime.UtcNow)));

        await consumir.Should().NotThrowAsync();
        (await _db.CalificacionesReplicadas.CountAsync()).Should().Be(0);
    }
}
