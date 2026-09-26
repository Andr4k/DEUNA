using Deuna.Orders.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Guarda en la réplica la calificación que el cliente le dio al domiciliario y al
/// restaurante de un pedido entregado (rebanada 3 de servicios finalizados).
///
/// La pantalla muestra las dos notas por fila y Orders no puede leerlas de Feedback sin
/// acoplarse a otro servicio en una lectura: llegan por evento, como el resto de la réplica.
/// Sin este consumer las dos columnas salen en null.
/// </summary>
public class CalificacionRegistradaConsumer : IConsumer<CalificacionRegistrada>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<CalificacionRegistradaConsumer> _logger;

    public CalificacionRegistradaConsumer(OrdersDbContext db, ILogger<CalificacionRegistradaConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CalificacionRegistrada> context)
    {
        var evento = context.Message;

        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == evento.PedidoId, context.CancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning(
                "CalificacionRegistrada recibida para un pedido que no existe en Orders: PedidoId={PedidoId}",
                evento.PedidoId);
            return;
        }

        // Idempotencia: la encuesta es una por pedido, así que una reentrega del mensaje no
        // puede duplicar la fila. El índice único sobre PedidoId es la última red.
        var yaRegistrada = await _db.CalificacionesReplicadas
            .AnyAsync(c => c.PedidoId == evento.PedidoId, context.CancellationToken);

        if (yaRegistrada)
        {
            _logger.LogInformation(
                "Calificación duplicada ignorada para {Codigo}: el pedido ya estaba calificado",
                evento.Codigo);
            return;
        }

        _db.CalificacionesReplicadas.Add(new CalificacionReplicada
        {
            PedidoId = evento.PedidoId,
            RepartidorId = evento.RepartidorId,
            CalificacionDomiciliario = evento.CalificacionDomiciliario,
            CalificacionRestaurante = evento.CalificacionRestaurante,
            FechaCalificacion = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        });

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Calificación de {Codigo} replicada: domiciliario={Domiciliario} restaurante={Restaurante}",
            pedido.Codigo, evento.CalificacionDomiciliario, evento.CalificacionRestaurante);
    }
}
