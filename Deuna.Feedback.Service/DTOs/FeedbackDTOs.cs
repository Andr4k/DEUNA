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

/// <summary>
/// Un criterio evaluado del Nivel 2 (US-004.2). El nombre del criterio es texto libre a
/// propósito: FR-004.4 exige poder agregar criterios sin alterar el esquema.
/// </summary>
public record CriterioDetalladoDto(string? Criterio, int Puntaje);

/// <summary>
/// Feedback Nivel 2 (US-004.2): criterios detallados. Es opcional (FR-004.2), pero si se
/// envía tiene que traer al menos un criterio evaluado.
/// </summary>
public record SubmitDetailedFeedbackRequest(
    Guid PedidoId,
    List<CriterioDetalladoDto> Criterios,
    string? Comentario,
    string? FotoUrl
);

/// <summary>
/// Respuesta del Nivel 2. <c>PuedeCompartirWhatsApp</c> es lo que decide si se muestra el
/// botón de compartir (FR-004.5).
/// </summary>
public record SubmitDetailedFeedbackResponse(
    Guid EncuestaId,
    string Mensaje,
    bool PuedeCompartirWhatsApp
);

/// <summary>Compartir la recomendación por WhatsApp (US-004.3).</summary>
public record ShareWhatsAppRequest(Guid PedidoId);

/// <summary>
/// Enlace corto y mensaje prellenado. <c>YaCompartido</c> distingue el primer compartido de
/// un reintento (el cliente tocó el botón dos veces): en los dos casos el enlace es el mismo.
/// </summary>
public record ShareWhatsAppResponse(
    string Enlace,
    string Mensaje,
    bool YaCompartido
);
