using Deuna.Delivery.Service.Consumers;
using Deuna.Delivery.Service.Models;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Delivery.Service.Tests.Consumers;

/// <summary>
/// El consumer de PedidoCancelado: la proyección de Delivery se entera de que Orders
/// canceló el pedido.
///
/// Antes no existía, y por eso un pedido cancelado llegaba a Delivery como "Buscando" y
/// se asignaba. Peor: si ya tenía domiciliario, lo dejaba ocupado para siempre.
///
/// El domiciliario se libera SOLO: el conjunto que lo ocupa es
/// [Asignado, ConfirmadoEnLocal, EnRuta] y "Cancelado" no está ahí. Eso se prueba en la
/// suite de asignación, que tiene Redis para el matching; acá se prueba la proyección.
/// </summary>
public class PedidoCanceladoConsumerTests
{
    private static readonly Guid PedidoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RepartidorId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static DeliveryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"pedido-cancelado-{Guid.NewGuid()}")
            .Options);

    private static PedidoCancelado CreateEvent(string motivo = "El restaurante se quedó sin insumos") => new(
        PedidoId: PedidoId,
        Codigo: "PED-00042",
        Motivo: motivo,
        OccurredAt: DateTime.UtcNow);

    private static PedidoDisponible CreatePedido(string estado, Guid? repartidorId = null) => new()
    {
        PedidoId = PedidoId,
        Codigo = "PED-00042",
        ClienteId = Guid.NewGuid(),
        RestauranteId = Guid.NewGuid(),
        Total = 62000m,
        Estado = estado,
        TokenQrLocal = "qr-local-abc123",
        TokenQrEntrega = "qr-entrega-abc123",
        Latitud = 4.60971,
        Longitud = -74.08175,
        RepartidorId = repartidorId,
        AsignadoAt = repartidorId is null ? null : DateTime.UtcNow.AddMinutes(-5)
    };

    private static async Task EjecutarAsync(DeliveryDbContext db, PedidoCancelado evento)
    {
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new PedidoCanceladoConsumer(db, NullLogger<PedidoCanceladoConsumer>.Instance));

        await harness.Start();
        try
        {
            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.Any<PedidoCancelado>()).Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_PedidoBuscando_MarcaLaProyeccionComoCancelada()
    {
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(PedidoDisponible.EstadoBuscando));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Estado.Should().Be(PedidoDisponible.EstadoCancelado);
    }

    [Fact]
    public async Task Consume_PedidoYaAsignado_LoCancelaYConservaQuienLoTenia()
    {
        // El domiciliario no se borra: el historial de quién lo tenía cuando se canceló
        // sirve. Lo que cambia es que deja de ocuparlo, y eso se prueba en la asignación.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(PedidoDisponible.EstadoAsignado, RepartidorId));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Estado.Should().Be(PedidoDisponible.EstadoCancelado);
        pedido.RepartidorId.Should().Be(RepartidorId, "quién lo tenía es parte del historial");
    }

    [Fact]
    public async Task Consume_PedidoYaEntregado_NoLoDeshace()
    {
        // Una cancelación que llega tarde no puede borrar un hecho consumado: la comida se
        // entregó. Pisarlo dejaría un pedido entregado marcado como cancelado.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(PedidoDisponible.EstadoEntregado, RepartidorId));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Estado.Should().Be(PedidoDisponible.EstadoEntregado);
    }

    [Fact]
    public async Task Consume_PedidoNoProyectado_NoRompe()
    {
        // Puede pasar: el PedidoCreado se perdió y la cancelación llegó igual. No hay nada
        // que cancelar y el mensaje no puede quedar en bucle de reintento por eso.
        var db = CreateDbContext();

        await EjecutarAsync(db, CreateEvent());

        (await db.PedidosDisponibles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consume_Reentrega_EsIdempotente()
    {
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(PedidoDisponible.EstadoAsignado, RepartidorId));
        await db.SaveChangesAsync();

        var evento = CreateEvent();
        await EjecutarAsync(db, evento);
        await EjecutarAsync(db, evento);

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Estado.Should().Be(PedidoDisponible.EstadoCancelado);
        (await db.PedidosDisponibles.CountAsync()).Should().Be(1, "una reentrega no duplica nada");
    }
}
