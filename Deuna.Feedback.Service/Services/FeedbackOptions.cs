namespace Deuna.Feedback.Service.Services;

/// <summary>
/// Configuración del servicio de feedback. Lo que depende del entorno vive acá: el enlace
/// que se comparte tiene que apuntar al dominio real en cada ambiente.
/// </summary>
public class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Base del enlace corto que se comparte por WhatsApp (US-004.3).</summary>
    public string UrlBaseCompartir { get; set; } = "https://deuna.app/r/";

    /// <summary>
    /// Plantilla del mensaje prellenado. {0} es el restaurante y {1} el enlace.
    /// </summary>
    public string PlantillaMensajeCompartir { get; set; } =
        "¡Acabo de pedir en {0} y estuvo increíble! 🍕 Prueba tú también: {1}";

    /// <summary>Nombre que se usa cuando la réplica del restaurante todavía no llegó.</summary>
    public string NombreRestaurantePorDefecto { get; set; } = "el restaurante";

    /// <summary>Rating mínimo de comida para habilitar el compartido (FR-004.5).</summary>
    public int RatingMinimoParaCompartir { get; set; } = 4;
}
