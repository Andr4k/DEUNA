using Deuna.Feedback.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Consumers;

/// <summary>
/// Replica en <c>repartidores_replicados</c> los repartidores registrados en Identity.
///
/// La encuesta guarda a qué repartidor se califica; sin la réplica ese identificador
/// llega sin perfil detrás y el restaurante no puede saber a quién corresponde.
/// Idempotente: si el mensaje se reentrega, actualiza el perfil en lugar de duplicar.
/// </summary>
public class RepartidorRegistradoConsumer : IConsumer<RepartidorRegistrado>
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<RepartidorRegistradoConsumer> _logger;

    public RepartidorRegistradoConsumer(FeedbackDbContext db, ILogger<RepartidorRegistradoConsumer> logger)
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
            existente.NombreCompleto = evento.NombreCompleto;
            existente.DocumentoIdentidad = evento.DocumentoIdentidad;
            existente.CiudadOperacion = evento.CiudadOperacion;
            existente.FotoPerfilUrl = evento.FotoPerfilUrl;
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Repartidor {RepartidorId} actualizado en la réplica", evento.RepartidorId);
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
            "Repartidor {RepartidorId} ({Nombre}) replicado desde Identity",
            evento.RepartidorId, evento.NombreCompleto);
    }
}
