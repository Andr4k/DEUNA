using Deuna.Orders.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Replica en <c>repartidores_replicados</c> los domiciliarios registrados en Identity.
///
/// El pedido ya se replica completo en Orders —estado, montos, fechas del ciclo— y la
/// asignación llega en <c>pedidos_asignados_repartidor</c>, pero ahí solo hay un
/// <c>RepartidorId</c>: sin este consumer la pantalla de servicios finalizados no puede
/// mostrar el nombre del domiciliario sin consultar a Delivery.
///
/// Idempotente: si el mensaje se reentrega, actualiza el perfil en lugar de duplicar.
/// </summary>
public class RepartidorRegistradoConsumer : IConsumer<RepartidorRegistrado>
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<RepartidorRegistradoConsumer> _logger;

    public RepartidorRegistradoConsumer(OrdersDbContext db, ILogger<RepartidorRegistradoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RepartidorRegistrado> context)
    {
        var evento = context.Message;

        var existente = await _db.RepartidoresReplicados
            .FirstOrDefaultAsync(r => r.Id == evento.RepartidorId, context.CancellationToken);

        if (existente is not null)
        {
            // Reenvío o actualización del perfil: se refrescan los datos, sin duplicar.
            existente.NombreCompleto = evento.NombreCompleto;
            existente.DocumentoIdentidad = evento.DocumentoIdentidad;
            existente.CiudadOperacion = evento.CiudadOperacion;
            existente.FotoPerfilUrl = evento.FotoPerfilUrl;
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Domiciliario {RepartidorId} actualizado en la réplica", evento.RepartidorId);
            return;
        }

        _db.RepartidoresReplicados.Add(new RepartidorReplicado
        {
            Id = evento.RepartidorId,
            NombreCompleto = evento.NombreCompleto,
            DocumentoIdentidad = evento.DocumentoIdentidad,
            CiudadOperacion = evento.CiudadOperacion,
            FotoPerfilUrl = evento.FotoPerfilUrl,
            Activo = true,
            CreatedAt = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        });

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Domiciliario {RepartidorId} ({Nombre}) replicado desde Identity",
            evento.RepartidorId, evento.NombreCompleto);
    }
}
