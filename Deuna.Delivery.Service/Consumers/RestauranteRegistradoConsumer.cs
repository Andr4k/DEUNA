using Deuna.Delivery.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Delivery.Service.Consumers;

/// <summary>
/// Replica en <c>restaurantes_replicados</c> los restaurantes registrados en Identity.
///
/// Sin esta réplica el mapa no tiene la tercera capa: los restaurantes viven en Identity y
/// Delivery necesita su dirección de sede para ubicarlos. No se guardan coordenadas del
/// evento a propósito —los restaurantes son fijos y se ubican por su dirección—, así que
/// la posición la resuelve el geocodificador y queda cacheada.
///
/// Idempotente: si el mensaje se reentrega, actualiza el perfil en lugar de duplicar.
/// </summary>
public class RestauranteRegistradoConsumer : IConsumer<RestauranteRegistrado>
{
    private readonly DeliveryDbContext _db;
    private readonly ILogger<RestauranteRegistradoConsumer> _logger;

    public RestauranteRegistradoConsumer(DeliveryDbContext db, ILogger<RestauranteRegistradoConsumer> logger)
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
            existente.NombreComercial = evento.NombreComercial;
            existente.DireccionSede = evento.DireccionSede;
            existente.Ciudad = evento.Ciudad;
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Restaurante {RestauranteId} actualizado en la réplica", evento.RestauranteId);
            return;
        }

        _db.RestaurantesReplicados.Add(new RestauranteReplicado
        {
            Id = evento.RestauranteId,
            NombreComercial = evento.NombreComercial,
            DireccionSede = evento.DireccionSede,
            Ciudad = evento.Ciudad,
            Activo = true,
            CreatedAt = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt
        });

        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Restaurante {RestauranteId} ({Nombre}) replicado desde Identity",
            evento.RestauranteId, evento.NombreComercial);
    }
}
