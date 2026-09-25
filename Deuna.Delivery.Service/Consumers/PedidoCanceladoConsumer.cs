using Deuna.Delivery.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Delivery.Service.Consumers;

/// <summary>
/// Consume el evento PedidoCancelado publicado por Orders y marca la proyección local como
/// cancelada.
///
/// Sin este consumer, un pedido cancelado llegaba a Delivery como "Buscando" y se asignaba;
/// y si ya tenía domiciliario, lo dejaba ocupado para siempre, porque el pedido seguía
/// contando como una entrega activa.
///
/// El domiciliario queda libre solo: el conjunto que lo ocupa es
/// [Asignado, ConfirmadoEnLocal, EnRuta] y "Cancelado" no está ahí. No hace falta código
/// extra para liberarlo, y ese es el punto: la liberación no es un paso que alguien pueda
/// olvidar.
/// </summary>
public class PedidoCanceladoConsumer : IConsumer<PedidoCancelado>
{
    private readonly DeliveryDbContext _db;
    private readonly ILogger<PedidoCanceladoConsumer> _logger;

    public PedidoCanceladoConsumer(DeliveryDbContext db, ILogger<PedidoCanceladoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoCancelado> context)
    {
        var evento = context.Message;

        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            // Puede pasar: el PedidoCreado se perdió y la cancelación llegó igual. No hay nada
            // que cancelar, y dejar el mensaje en bucle de reintento no arreglaría nada.
            _logger.LogWarning(
                "PedidoCancelado recibido para un pedido no proyectado: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        if (pedido.Estado == PedidoDisponible.EstadoCancelado)
        {
            _logger.LogInformation(
                "Cancelación duplicada ignorada para {Codigo}: ya estaba cancelado", pedido.Codigo);
            return;
        }

        // Una cancelación que llega tarde no deshace un hecho consumado: si la comida ya se
        // entregó, pisar el estado dejaría un pedido entregado marcado como cancelado.
        if (pedido.Estado == PedidoDisponible.EstadoEntregado)
        {
            _logger.LogWarning(
                "Cancelación ignorada para {Codigo}: ya fue entregado, y eso no se deshace",
                pedido.Codigo);
            return;
        }

        pedido.Estado = PedidoDisponible.EstadoCancelado;

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} cancelado en la proyección de Delivery (motivo: {Motivo}). El domiciliario {RepartidorId} queda libre",
            pedido.Codigo, evento.Motivo, pedido.RepartidorId);
    }
}
