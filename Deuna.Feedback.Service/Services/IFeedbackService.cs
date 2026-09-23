using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;

namespace Deuna.Feedback.Service.Services;

/// <summary>Resultado posible del registro de feedback Nivel 1.</summary>
public enum ResultadoFeedback
{
    Ok,
    PedidoNoEncontrado,
    PedidoNoEntregado,
    FeedbackDuplicado
}

public record ResultadoEnvioFeedback(ResultadoFeedback Resultado, FeedbackEncuesta? Encuesta = null);

public interface IFeedbackService
{
    /// <summary>
    /// Registra el feedback Nivel 1 de un pedido. Solo se acepta si el pedido existe,
    /// está en estado <c>Entregado</c> y no tiene feedback previo (FR-004.1).
    /// </summary>
    Task<ResultadoEnvioFeedback> EnviarFeedbackBasicoAsync(SubmitBasicFeedbackRequest request, CancellationToken cancellationToken = default);
}
