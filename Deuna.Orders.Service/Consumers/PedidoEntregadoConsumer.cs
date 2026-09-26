using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Cierra el pedido cuando Delivery confirma la entrega (TASK-305).
///
/// Orders es el dueño del estado del pedido: sin este consumer, el restaurante seguiría
/// viendo el pedido como recién creado después de que el cliente ya lo recibió.
/// </summary>
public class PedidoEntregadoConsumer : IConsumer<PedidoEntregado>
{
    /// <summary>Estado terminal del pedido.</summary>
    public const string EstadoEntregado = EstadosPedido.Entregado;

    private readonly OrdersDbContext _db;
    private readonly ILogger<PedidoEntregadoConsumer> _logger;

    public PedidoEntregadoConsumer(OrdersDbContext db, ILogger<PedidoEntregadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoEntregado> context)
    {
        var evento = context.Message;

        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "PedidoEntregado recibido para un pedido que no existe en Orders: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        // Cierra el intento vigente: es el domiciliario que efectivamente entregó.
        var asignacion = await AsignacionesDelPedido.VigenteAsync(
            _db, evento.PedidoId, context.CancellationToken);

        if (asignacion is not null)
        {
            asignacion.FechaEntrega ??= evento.FechaEntrega;
            asignacion.EstadoAsignacion = AsignacionReplicada.EstadoCompletado;
        }

        // Idempotente: una reentrega del mensaje no puede reescribir la fecha de entrega.
        if (pedido.Estado == EstadoEntregado)
        {
            _logger.LogInformation(
                "El pedido {Codigo} ya estaba marcado como entregado: no se reescribe la fecha",
                pedido.Codigo);

            await _db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        pedido.Estado = EstadoEntregado;
        pedido.FechaEntrega = evento.FechaEntrega;

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} cerrado como entregado (repartidor {RepartidorId})",
            pedido.Codigo, evento.RepartidorId);
    }
}
