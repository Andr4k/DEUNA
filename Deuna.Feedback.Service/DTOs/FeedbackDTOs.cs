using System.ComponentModel.DataAnnotations;

namespace Deuna.Feedback.Service.DTOs;

/// <summary>
/// Feedback Nivel 1 (US-004.1): ratings básicos en menos de 5 segundos.
/// </summary>
public record SubmitBasicFeedbackRequest(
    [Required] Guid PedidoId,
    [Required] int RatingGeneralComida,
    [Required] int RatingServicioRepartidor,
    bool DeseaRecomendar
);

/// <summary>
/// Respuesta del Nivel 1. <c>MostrarNivel2</c> habilita el flujo detallado
/// cuando la calificación de comida es alta (FR-004.5).
/// </summary>
public record SubmitBasicFeedbackResponse(
    Guid EncuestaId,
    string Mensaje,
    bool MostrarNivel2
);
