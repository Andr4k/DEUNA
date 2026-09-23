using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Feedback.Service.Services;

/// <summary>
/// Registro del feedback Nivel 1 (US-004.1, FR-004.1).
///
/// Valida contra la proyección local de pedidos (sin acoplamiento síncrono con Orders):
/// el pedido debe existir, estar <c>Entregado</c> y no tener feedback previo.
/// </summary>
public class FeedbackService : IFeedbackService
{
    private readonly FeedbackDbContext _db;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(FeedbackDbContext db, ILogger<FeedbackService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ResultadoEnvioFeedback> EnviarFeedbackBasicoAsync(
        SubmitBasicFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosReplicados
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PedidoId == request.PedidoId, cancellationToken);

        if (pedido is null)
        {
            _logger.LogWarning("Feedback rechazado: el pedido {PedidoId} no existe en la proyección", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.PedidoNoEncontrado);
        }

        if (!string.Equals(pedido.Estado, PedidoReplicado.EstadoEntregado, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Feedback rechazado: el pedido {PedidoId} está en estado {Estado} y no Entregado",
                request.PedidoId, pedido.Estado);
            return new ResultadoEnvioFeedback(ResultadoFeedback.PedidoNoEntregado);
        }

        var yaTieneFeedback = await _db.FeedbackEncuestas
            .AnyAsync(e => e.PedidoId == request.PedidoId, cancellationToken);

        if (yaTieneFeedback)
        {
            _logger.LogWarning("Feedback rechazado: el pedido {PedidoId} ya tiene feedback registrado", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.FeedbackDuplicado);
        }

        var encuesta = new FeedbackEncuesta
        {
            PedidoId = pedido.PedidoId,
            RestauranteId = pedido.RestauranteId,
            RepartidorId = pedido.RepartidorId,
            RatingGeneralComida = request.RatingGeneralComida,
            RatingServicioRepartidor = request.RatingServicioRepartidor,
            DeseaRecomendar = request.DeseaRecomendar
        };

        _db.FeedbackEncuestas.Add(encuesta);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // El índice único sobre PedidoId cubre la carrera de dos envíos simultáneos:
            // el segundo se traduce en "ya tiene feedback" en lugar de un 500.
            _logger.LogWarning(ex, "Feedback duplicado detectado al guardar para el pedido {PedidoId}", request.PedidoId);
            return new ResultadoEnvioFeedback(ResultadoFeedback.FeedbackDuplicado);
        }

        _logger.LogInformation(
            "Feedback Nivel 1 registrado: EncuestaId={EncuestaId} PedidoId={PedidoId} Comida={Comida} Repartidor={Repartidor}",
            encuesta.Id, encuesta.PedidoId, encuesta.RatingGeneralComida, encuesta.RatingServicioRepartidor);

        return new ResultadoEnvioFeedback(ResultadoFeedback.Ok, encuesta);
    }
}
