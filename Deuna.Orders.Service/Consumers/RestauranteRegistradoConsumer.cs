using Deuna.Orders.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Replica en <c>restaurantes_replicados</c> los restaurantes registrados en Identity.
///
/// Sin esta replicación, un restaurante recién registrado no existe para Orders y todo
/// intento de crear un pedido falla con "restaurante no encontrado": el alta en Identity
/// y la operación de pedidos quedaban desconectadas.
///
/// Idempotente: si el mensaje se reentrega, actualiza los datos en lugar de duplicar.
/// </summary>
public class RestauranteRegistradoConsumer : IConsumer<RestauranteRegistrado>
{
    /// <summary>Radio de cobertura por defecto (el flujo indica 5 km, configurable).</summary>
    private const decimal RadioCoberturaPorDefectoKm = 5m;

    private readonly OrdersDbContext _db;
    private readonly ILogger<RestauranteRegistradoConsumer> _logger;

    public RestauranteRegistradoConsumer(OrdersDbContext db, ILogger<RestauranteRegistradoConsumer> logger)
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
            // Reenvío o actualización del perfil: se refrescan los datos, sin duplicar.
            existente.NombreComercial = evento.NombreComercial;
            existente.RazonSocial = evento.RazonSocial;
            existente.Nit = evento.Nit;
            existente.DireccionSede = evento.DireccionSede;
            existente.Ciudad = evento.Ciudad;
            existente.Latitud = (decimal)evento.Latitud;
            existente.Longitud = (decimal)evento.Longitud;
            existente.Ubicacion = CrearPunto(evento.Latitud, evento.Longitud);
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Restaurante {RestauranteId} actualizado en la réplica", evento.RestauranteId);
            return;
        }

        var restaurante = new RestauranteReplicado
        {
            Id = evento.RestauranteId,
            NombreComercial = evento.NombreComercial,
            RazonSocial = evento.RazonSocial,
            Nit = evento.Nit,
            DireccionSede = evento.DireccionSede,
            Ciudad = evento.Ciudad,
            Latitud = (decimal)evento.Latitud,
            Longitud = (decimal)evento.Longitud,
            Ubicacion = CrearPunto(evento.Latitud, evento.Longitud),
            RadioCoberturaKm = RadioCoberturaPorDefectoKm,
            AceptaPedidos = true,
            Activo = true,
            HoraApertura = TimeSpan.Zero,
            HoraCierre = new TimeSpan(23, 59, 0),
            CreatedAt = evento.OccurredAt == default ? DateTime.UtcNow : evento.OccurredAt,
            // Horario por defecto para todos los días: el restaurante todavía no tiene
            // endpoints para configurar su horario, y sin filas de horario el alta de
            // pedidos se rechaza con "fuera de horario de atención".
            HorariosAtencion = Enum.GetValues<DayOfWeek>()
                .Select(dia => new HorarioAtencionReplicado
                {
                    RestauranteReplicadoId = evento.RestauranteId,
                    DiaSemana = dia.ToString(),
                    HoraInicio = TimeSpan.Zero,
                    HoraFin = new TimeSpan(23, 59, 0),
                    Cerrado = false
                })
                .ToList()
        };

        _db.RestaurantesReplicados.Add(restaurante);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Restaurante {RestauranteId} ({Nombre}) replicado desde Identity",
            evento.RestauranteId, evento.NombreComercial);
    }

    private static Point CrearPunto(double latitud, double longitud)
        => new(longitud, latitud) { SRID = 4326 };
}
