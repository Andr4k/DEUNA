using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Registra la asignación del pedido a un domiciliario (TASK-308).
///
/// Orders es el dueño del estado del pedido: sin este consumer, el restaurante vería el
/// pedido como recién creado hasta que se entregara, sin saber que ya salió a buscarlo
/// alguien ni quién.
/// </summary>
public class PedidoAsignadoConsumer : IConsumer<PedidoAsignado>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<PedidoAsignadoConsumer> _logger;

    public PedidoAsignadoConsumer(OrdersDbContext db, ILogger<PedidoAsignadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoAsignado> context)
    {
        var evento = context.Message;

        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "PedidoAsignado recibido para un pedido que no existe en Orders: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        var fechaAsignacion = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt;

        // Idempotencia: una reentrega del mensaje trae el mismo OccurredAt, así que el intento
        // no se duplica. Un mismo domiciliario sí puede volver a recibir el pedido más
        // adelante (tras un rechazo y un reintento): eso es otro intento, con otra hora.
        var yaRegistrado = await _db.AsignacionesReplicadas.AnyAsync(
            a => a.PedidoId == evento.PedidoId
                 && a.RepartidorId == evento.RepartidorId
                 && a.FechaAsignacion == fechaAsignacion,
            context.CancellationToken);

        if (yaRegistrado)
        {
            _logger.LogInformation(
                "Asignación duplicada ignorada para {Codigo}: el intento ya estaba registrado",
                evento.Codigo);
            return;
        }

        _db.AsignacionesReplicadas.Add(new AsignacionReplicada
        {
            PedidoId = evento.PedidoId,
            RepartidorId = evento.RepartidorId,
            FechaAsignacion = fechaAsignacion,
            EstadoAsignacion = AsignacionReplicada.EstadoAsignado
        });

        pedido.Estado = EstadosPedido.Asignado;

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} asignado al domiciliario {RepartidorId}", pedido.Codigo, evento.RepartidorId);
    }
}
