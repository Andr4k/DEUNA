using Deuna.Delivery.Service.Consumers;
using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Deuna.Delivery.Service.Tests.Consumers;

/// <summary>
/// Reparación de filas proyectadas antes de que existieran las columnas de punto.
///
/// La migración <c>20260923070403_AddPedidoLocationColumns</c> llegó después de que
/// algunas filas ya estuvieran proyectadas: esas quedaron con Latitud/Longitud en null y
/// el consumer, al ver la fila ya existente, salía sin actualizar. Una fila sin punto no
/// se puede asignar (AsignacionService la rechaza) y el pedido queda varado para siempre,
/// aunque el evento se republique con el punto ya presente.
///
/// Acá se prueba la reparación: la reentrega de un PedidoCreado con punto completa una fila
/// incompleta, y no toca una fila que ya tiene su punto.
/// </summary>
public class PedidoCreadoConsumerReparacionTests
{
    // El pedido real de la incidencia (PED-20260923-000001).
    private static readonly Guid PedidoId = Guid.Parse("4f8bf4cc-095c-4e4a-8b40-b61e9ffed104");
    private static readonly Guid ClienteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RestauranteId = Guid.Parse("a1b2c3d4-5e6f-4a8b-9c0d-1e2f3a4b5c6d");

    // Punto del evento: lng -74.0817, lat 4.6097.
    private const double LatitudEvento = 4.6097;
    private const double LongitudEvento = -74.0817;

    // Punto previo distinto, para que "no se tocó" sea observable.
    private const double LatitudPrevia = 6.2442;
    private const double LongitudPrevia = -75.5812;

    private static DeliveryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"pedido-creado-reparacion-{Guid.NewGuid()}")
            .Options);

    private static PedidoCreado CreateEvent(double? latitud = LatitudEvento, double? longitud = LongitudEvento) => new(
        PedidoId: PedidoId,
        Codigo: "PED-20260923-000001",
        ClienteId: ClienteId,
        RestauranteId: RestauranteId,
        Total: 62000m,
        Estado: "Pendiente",
        TokenQrLocal: "qr-local-abc123",
        TokenQrEntrega: "qr-entrega-abc123",
        OccurredAt: DateTime.UtcNow,
        Latitud: latitud,
        Longitud: longitud);

    private static PedidoDisponible CreatePedido(double? latitud, double? longitud) => new()
    {
        PedidoId = PedidoId,
        Codigo = "PED-20260923-000001",
        ClienteId = ClienteId,
        RestauranteId = RestauranteId,
        Total = 62000m,
        Estado = PedidoDisponible.EstadoBuscando,
        TokenQrLocal = "qr-local-abc123",
        TokenQrEntrega = "qr-entrega-abc123",
        Latitud = latitud,
        Longitud = longitud
    };

    private static IAsignacionService AsignacionSinCandidatos()
    {
        var asignacion = new Mock<IAsignacionService>();
        asignacion
            .Setup(a => a.IntentarAsignarAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoAsignacion(false, "sin candidatos (doble de test)"));
        return asignacion.Object;
    }

    private static async Task EjecutarAsync(DeliveryDbContext db, PedidoCreado evento)
    {
        var harness = new InMemoryTestHarness();
        harness.Consumer(() => new PedidoCreadoConsumer(db, AsignacionSinCandidatos(), NullLogger<PedidoCreadoConsumer>.Instance));

        await harness.Start();
        try
        {
            await harness.InputQueueSendEndpoint.Send(evento);
            (await harness.Consumed.Any<PedidoCreado>()).Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_FilaExistenteSinPunto_EventoConPunto_CompletaLaFila()
    {
        // La fila se proyectó antes de la migración: sin punto no se puede asignar.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(latitud: null, longitud: null));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Latitud.Should().Be(LatitudEvento);
        pedido.Longitud.Should().Be(LongitudEvento);
        pedido.Estado.Should().Be(PedidoDisponible.EstadoBuscando, "la reparación no cambia el estado");
    }

    [Fact]
    public async Task Consume_FilaExistenteConPunto_NoSobrescribeElPunto()
    {
        // Una fila completa no se reescribe: el punto que ya tiene es el bueno.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(LatitudPrevia, LongitudPrevia));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Latitud.Should().Be(LatitudPrevia, "una fila que ya tiene punto no se toca");
        pedido.Longitud.Should().Be(LongitudPrevia, "una fila que ya tiene punto no se toca");
    }

    [Fact]
    public async Task Consume_FilaExistenteSinPunto_EventoSinPunto_NoRompe()
    {
        // Un mensaje v1 encolado no trae punto: no hay nada que reparar y el mensaje no
        // puede quedar en bucle de reintento por eso.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(latitud: null, longitud: null));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent(latitud: null, longitud: null));

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Latitud.Should().BeNull();
        pedido.Longitud.Should().BeNull();
        (await db.PedidosDisponibles.CountAsync()).Should().Be(1, "no se duplica la fila");
    }

    [Fact]
    public async Task Consume_FilaExistenteSinPunto_EventoConPuntoParcial_CompletaLoQueFalta()
    {
        // Solo falta la longitud: el evento trae el punto completo y la fila queda usable.
        var db = CreateDbContext();
        db.PedidosDisponibles.Add(CreatePedido(LatitudEvento, longitud: null));
        await db.SaveChangesAsync();

        await EjecutarAsync(db, CreateEvent());

        var pedido = await db.PedidosDisponibles.SingleAsync();
        pedido.Latitud.Should().Be(LatitudEvento);
        pedido.Longitud.Should().Be(LongitudEvento);
    }
}
