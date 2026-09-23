using Deuna.Feedback.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Consumers;

/// <summary>
/// Proyecta en <c>pedidos_replicados</c> los pedidos creados por Orders.
/// Es la base de la validación del feedback: sin esta proyección no se puede
/// saber si un pedido existe ni en qué estado está (ADR-005, replicación por eventos).
///
/// Idempotente: si el mensaje se reentrega, no duplica la fila.
/// </summary>
public class PedidoCreadoConsumer : IConsumer<PedidoCreado>
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<PedidoCreadoConsumer> _logger;

    public PedidoCreadoConsumer(FeedbackDbContext db, ILogger<PedidoCreadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoCreado> context)
    {
        var evento = context.Message;

        if (await _db.PedidosReplicados.AnyAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken))
        {
            _logger.LogWarning("PedidoCreado duplicado ignorado: PedidoId={PedidoId}", evento.PedidoId);
            return;
        }

        _db.PedidosReplicados.Add(new PedidoReplicado
        {
            PedidoId = evento.PedidoId,
            Codigo = evento.Codigo,
            ClienteId = evento.ClienteId,
            RestauranteId = evento.RestauranteId,
            Estado = string.IsNullOrWhiteSpace(evento.Estado) ? PedidoReplicado.EstadoPendiente : evento.Estado,
            Total = evento.Total,
            FechaCreacion = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt,
            CorrelationId = context.CorrelationId
        });

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {PedidoId} ({Codigo}) proyectado para feedback con estado {Estado}",
            evento.PedidoId, evento.Codigo, evento.Estado);
    }
}

/// <summary>
/// Registra el repartidor asignado al pedido. Necesario porque
/// <c>feedback_encuestas.repartidor_id</c> alimenta las métricas del repartidor.
/// </summary>
public class PedidoAsignadoConsumer : IConsumer<PedidoAsignado>
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<PedidoAsignadoConsumer> _logger;

    public PedidoAsignadoConsumer(FeedbackDbContext db, ILogger<PedidoAsignadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoAsignado> context)
    {
        var evento = context.Message;

        var pedido = await _db.PedidosReplicados
            .FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            // El evento puede llegar antes que el PedidoCreado (o para un pedido no proyectado):
            // se registra y no se falla, porque el feedback se valida contra el estado del pedido.
            _logger.LogWarning(
                "PedidoAsignado recibido para un pedido no proyectado: PedidoId={PedidoId}", evento.PedidoId);
            return;
        }

        pedido.RepartidorId = evento.RepartidorId;
        pedido.ActualizadoEn = DateTime.UtcNow;
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Repartidor {RepartidorId} asociado al pedido {PedidoId}", evento.RepartidorId, evento.PedidoId);
    }
}

/// <summary>
/// Mantiene el estado del pedido al día. Es lo que permite aceptar feedback
/// únicamente cuando el pedido está <c>Entregado</c>.
/// </summary>
public class PedidoActualizadoConsumer : IConsumer<PedidoActualizado>
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<PedidoActualizadoConsumer> _logger;

    public PedidoActualizadoConsumer(FeedbackDbContext db, ILogger<PedidoActualizadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoActualizado> context)
    {
        var evento = context.Message;

        var pedido = await _db.PedidosReplicados
            .FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "PedidoActualizado recibido para un pedido no proyectado: PedidoId={PedidoId}", evento.PedidoId);
            return;
        }

        pedido.Estado = evento.EstadoNuevo;
        pedido.ActualizadoEn = DateTime.UtcNow;
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {PedidoId} pasó de {EstadoAnterior} a {EstadoNuevo}",
            evento.PedidoId, evento.EstadoAnterior, evento.EstadoNuevo);
    }
}

/// <summary>
/// Marca el pedido como entregado (TASK-305).
///
/// Es el evento terminal del ciclo: Delivery lo publica cuando el cliente escanea el QR de
/// cierre. Sin este consumer la proyección se quedaría en <c>EnRuta</c> y la encuesta nunca
/// se habilitaría, porque solo se acepta para pedidos entregados.
/// </summary>
public class PedidoEntregadoConsumer : IConsumer<PedidoEntregado>
{
    private const string EstadoEntregado = "Entregado";

    private readonly FeedbackDbContext _db;
    private readonly ILogger<PedidoEntregadoConsumer> _logger;

    public PedidoEntregadoConsumer(FeedbackDbContext db, ILogger<PedidoEntregadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoEntregado> context)
    {
        var evento = context.Message;

        var pedido = await _db.PedidosReplicados
            .FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "PedidoEntregado recibido para un pedido no proyectado: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        pedido.Estado = EstadoEntregado;
        pedido.ActualizadoEn = DateTime.UtcNow;

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} marcado como entregado: la encuesta queda habilitada", evento.Codigo);
    }
}
