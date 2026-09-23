using Deuna.Shared.Events;
using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
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
    private readonly IAsignacionService _asignacion;
    private readonly ILogger<PedidoCreadoConsumer> _logger;

    public PedidoCreadoConsumer(
        DeliveryDbContext db,
        IAsignacionService asignacion,
        ILogger<PedidoCreadoConsumer> logger)
    {
        _db = db;
        _asignacion = asignacion;
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
            Latitud = evento.Latitud,
            Longitud = evento.Longitud,
            FechaCreacion = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        };

        _db.PedidosDisponibles.Add(pedidoDisponible);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Pedido {PedidoId} proyectado en pedidos_disponibles con estado {Estado}",
            pedidoDisponible.PedidoId, pedidoDisponible.Estado);

        // La búsqueda del domiciliario arranca acá: el pedido no espera a que alguien lo
        // vea en una lista (ADR-006). Si no hay nadie en el radio queda en búsqueda y el
        // reintento periódico lo vuelve a intentar.
        //
        // Un fallo del matching (por ejemplo, Redis caído) NO debe hacer fallar el mensaje:
        // el pedido ya está proyectado y lo importante es que entre al sistema. El reintento
        // periódico se encarga de asignarlo cuando el matching vuelva a estar disponible.
        try
        {
            var asignacion = await _asignacion.IntentarAsignarAsync(evento.PedidoId, cancellationToken: context.CancellationToken);

            if (asignacion.Asignado)
            {
                _logger.LogInformation(
                    "Pedido {PedidoId} asignado automáticamente a {RepartidorId}",
                    evento.PedidoId, asignacion.RepartidorId);
            }
            else
            {
                _logger.LogInformation(
                    "Pedido {PedidoId} queda en búsqueda: {Motivo}",
                    evento.PedidoId, asignacion.Mensaje);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo intentar la asignación del pedido {PedidoId}: queda en búsqueda para el reintento",
                evento.PedidoId);
        }
    }
}
