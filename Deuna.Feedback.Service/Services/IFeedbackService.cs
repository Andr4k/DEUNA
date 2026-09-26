using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;

namespace Deuna.Feedback.Service.Services;

/// <summary>Resultado posible del registro de feedback Nivel 1.</summary>
public enum ResultadoFeedback
{
    Ok,
    PedidoNoEncontrado,
    PedidoNoEntregado,
    FeedbackDuplicado,

    /// <summary>El Nivel 2 solo existe sobre un Nivel 1 ya registrado (US-004.2).</summary>
    Nivel1Requerido,

    /// <summary>El Nivel 2 ya se envió para este pedido.</summary>
    DetalleDuplicado,

    /// <summary>El rating de comida no alcanza para habilitar el compartido (FR-004.5).</summary>
    RatingBajoParaCompartir,

    /// <summary>
    /// El rating de comida no alcanza para el Nivel 2: el flujo detallado es para quien
    /// quiere recomendar el restaurante (US-004.2), y por debajo de 4 estrellas la PWA ni
    /// lo ofrece (<c>mostrarNivel2</c> del Nivel 1).
    /// </summary>
    RatingBajoParaNivel2
}

public record ResultadoEnvioFeedback(ResultadoFeedback Resultado, FeedbackEncuesta? Encuesta = null);

public record ResultadoEnvioDetallado(
    ResultadoFeedback Resultado,
    FeedbackEncuesta? Encuesta = null,
    bool PuedeCompartirWhatsApp = false);

public record ResultadoCompartir(
    ResultadoFeedback Resultado,
    string? Enlace = null,
    string? Mensaje = null,
    bool YaCompartido = false);

public interface IFeedbackService
{
    /// <summary>
    /// Registra el feedback Nivel 1 de un pedido. Solo se acepta si el pedido existe,
    /// está en estado <c>Entregado</c> y no tiene feedback previo (FR-004.1).
    /// </summary>
    Task<ResultadoEnvioFeedback> EnviarFeedbackBasicoAsync(SubmitBasicFeedbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra el feedback Nivel 2: criterios detallados, comentario y foto opcionales.
    /// Requiere que el Nivel 1 ya esté registrado (US-004.2) y no admite un segundo envío.
    /// </summary>
    Task<ResultadoEnvioDetallado> EnviarFeedbackDetalladoAsync(SubmitDetailedFeedbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra el compartido por WhatsApp y devuelve el enlace corto con el mensaje
    /// prellenado (US-004.3). Solo con rating de comida alto (FR-004.5), e idempotente:
    /// volver a compartir devuelve el mismo enlace.
    /// </summary>
    Task<ResultadoCompartir> RegistrarCompartidoWhatsAppAsync(ShareWhatsAppRequest request, CancellationToken cancellationToken = default);
}
