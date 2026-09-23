using Deuna.Shared.Events;
using Deuna.Delivery.Service.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Delivery.Service.Consumers;

/// <summary>
/// Consume el evento PedidoCreado publicado por Orders Service y proyecta el
/// pedido en la tabla local pedidos_disponibles con estado "Buscando", para que
/// los repartidores puedan verlo y aceptarlo.
///
/// Es idempotente: si el mensaje se reentrega, no duplica el registro.
/// </summary>
public class PedidoCreadoConsumer : IConsumer<PedidoCreado>
{
    private readonly DeliveryDbContext _db;
    private readonly ILogger<PedidoCreadoConsumer> _logger;

    public PedidoCreadoConsumer(DeliveryDbContext db, ILogger<PedidoCreadoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PedidoCreado> context)
    {
        var evento = context.Message;
        var correlationId = context.CorrelationId;

        _logger.LogInformation(
            "PedidoCreado recibido: CorrelationId={CorrelationId} PedidoId={PedidoId} RestauranteId={RestauranteId} Codigo={Codigo}",
            correlationId, evento.PedidoId, evento.RestauranteId, evento.Codigo);

        var yaProyectado = await _db.PedidosDisponibles
            .AnyAsync(p => p.PedidoId == evento.PedidoId, context.CancellationToken);

        if (yaProyectado)
        {
            _logger.LogWarning(
                "PedidoCreado duplicado ignorado: PedidoId={PedidoId} (ya estaba en pedidos_disponibles)",
                evento.PedidoId);
            return;
        }

        var pedidoDisponible = new PedidoDisponible
        {
            PedidoId = evento.PedidoId,
            Codigo = evento.Codigo,
            ClienteId = evento.ClienteId,
            RestauranteId = evento.RestauranteId,
            Total = evento.Total,
            Estado = PedidoDisponible.EstadoBuscando,
            QrCodigo = evento.QrCodigo,
            CorrelationId = correlationId,
            FechaCreacion = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        };

        _db.PedidosDisponibles.Add(pedidoDisponible);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {PedidoId} proyectado en pedidos_disponibles con estado {Estado}",
            pedidoDisponible.PedidoId, pedidoDisponible.Estado);
    }
}
