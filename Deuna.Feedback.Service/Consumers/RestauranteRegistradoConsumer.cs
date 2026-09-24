using Deuna.Feedback.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Consumers;

/// <summary>
/// Replica en <c>restaurantes_replicados</c> los restaurantes registrados en Identity.
///
/// El Nivel 3 lo necesita para nombrar al restaurante en el mensaje que el cliente comparte
/// por WhatsApp (US-004.3). Sin la réplica, el mensaje viral no puede decir en qué
/// restaurante comió, y consultarlo a Identity sería acoplamiento síncrono entre servicios.
///
/// Idempotente: si el mensaje se reentrega, actualiza los datos en lugar de duplicar.
/// </summary>
public class RestauranteRegistradoConsumer : IConsumer<RestauranteRegistrado>
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<RestauranteRegistradoConsumer> _logger;

    public RestauranteRegistradoConsumer(FeedbackDbContext db, ILogger<RestauranteRegistradoConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RestauranteRegistrado> context)
    {
        var evento = context.Message;

        var existente = await _db.RestaurantesReplicados
            .FirstOrDefaultAsync(r => r.Id == evento.RestauranteId, context.CancellationToken);

        if (existente is not null)
        {
            // Reenvío o actualización del perfil: se refresca el nombre, sin duplicar.
            existente.NombreComercial = evento.NombreComercial;
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Restaurante {RestauranteId} actualizado en la réplica", evento.RestauranteId);
            return;
        }

        _db.RestaurantesReplicados.Add(new RestauranteReplicado
        {
            Id = evento.RestauranteId,
            NombreComercial = evento.NombreComercial,
            Activo = true,
            CreatedAt = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        });

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Restaurante {RestauranteId} ({Nombre}) replicado desde Identity",
            evento.RestauranteId, evento.NombreComercial);
    }
}
