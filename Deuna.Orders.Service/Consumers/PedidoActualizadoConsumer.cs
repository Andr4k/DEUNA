using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Mantiene el estado del pedido al día con las transiciones del ciclo de entrega (TASK-308).
///
/// Delivery publica este evento al confirmar la llegada al local, al iniciar la entrega y al
/// registrar un rechazo. Sin este consumer, Orders mostraría el pedido en el estado en que
/// nació hasta que se entregara.
/// </summary>
public class PedidoActualizadoConsumer : IConsumer<PedidoActualizado>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<PedidoActualizadoConsumer> _logger;

    public PedidoActualizadoConsumer(OrdersDbContext db, ILogger<PedidoActualizadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoActualizado> context)
    {
        var evento = context.Message;

        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "PedidoActualizado recibido para un pedido que no existe en Orders: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        // Un rechazo devuelve el pedido a búsqueda, y Delivery reasigna en el acto publicando
        // PedidoAsignado. Los dos eventos viajan por colas distintas, así que no hay orden
        // garantizado: si el rechazo llega después de la reasignación, aplicarlo dejaría el
        // pedido en búsqueda estando ya asignado. Se detecta porque existe un intento
        // posterior al momento del rechazo.
        if (evento.EstadoNuevo == EstadosPedido.Buscando)
        {
            var hayIntentoPosterior = await _db.AsignacionesReplicadas.AnyAsync(
                a => a.PedidoId == evento.PedidoId && a.FechaAsignacion > evento.OccurredAt,
                context.CancellationToken);

            if (hayIntentoPosterior)
            {
                _logger.LogInformation(
                    "Rechazo antiguo ignorado para {Codigo}: el pedido ya tiene una asignación posterior",
                    evento.Codigo);
                return;
            }
        }

        var estadoAnterior = pedido.Estado;
        pedido.Estado = evento.EstadoNuevo;

        var asignacion = await AsignacionesDelPedido.VigenteAsync(
            _db, evento.PedidoId, context.CancellationToken);

        if (asignacion is not null)
        {
            // El estado del pedido vive en Pedido.Estado; estos hitos son la línea de tiempo
            // del intento. Se escriben una sola vez: una reentrega no reescribe la hora.
            switch (evento.EstadoNuevo)
            {
                case EstadosPedido.ConfirmadoEnLocal:
                    asignacion.FechaLlegadaLocal ??= evento.OccurredAt;
                    asignacion.EstadoAsignacion = AsignacionReplicada.EstadoEnLocal;
                    break;

                case EstadosPedido.EnRuta:
                    asignacion.FechaSalidaRuta ??= evento.OccurredAt;
                    asignacion.EstadoAsignacion = AsignacionReplicada.EstadoEnRuta;
                    break;

                case EstadosPedido.Buscando:
                    // Rechazo antes de llegar al local: el intento se cierra y queda el motivo.
                    asignacion.EstadoAsignacion = AsignacionReplicada.EstadoRechazado;
                    asignacion.MotivoRechazo = evento.Motivo;
                    break;
            }
        }

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} pasó de {EstadoAnterior} a {EstadoNuevo}",
            pedido.Codigo, estadoAnterior, evento.EstadoNuevo);
    }
}
