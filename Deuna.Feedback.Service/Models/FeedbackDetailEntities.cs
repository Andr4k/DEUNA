using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Feedback.Service.Models;

/// <summary>
/// Criterio evaluado del Nivel 2 (US-004.2).
///
/// Una fila por criterio, **no** una columna por criterio: FR-004.3 y FR-004.4 exigen
/// poder agregar criterios nuevos sin alterar el esquema. Con columnas, un criterio que
/// no estuviera previsto no tendría dónde guardarse.
/// </summary>
[Table("feedback_detalle_criterios")]
public class FeedbackDetalleCriterio
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EncuestaId { get; set; }

    /// <summary>
    /// Texto libre (Sabor, Temperatura, Presentacion, Cantidad, Empaque y los que vengan).
    /// La unicidad por encuesta se garantiza con un índice, no con una lista cerrada.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Criterio { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Puntaje { get; set; }
}

/// <summary>
/// Comentario y foto del Nivel 2 (US-004.2). Opcionales y 1:1 con la encuesta: si el
/// cliente no escribió nada ni adjuntó foto, la fila no existe.
/// </summary>
[Table("feedback_comentarios_fotos")]
public class FeedbackComentarioFoto
{
    [Key]
    public Guid EncuestaId { get; set; }

    [MaxLength(1000)]
    public string? ComentarioTexto { get; set; }

    [MaxLength(500)]
    public string? UrlFotoEvidencia { get; set; }
}

/// <summary>
/// Compartido por WhatsApp (US-004.3). 1:1 con la encuesta.
/// </summary>
[Table("feedback_viral_whatsapp")]
public class FeedbackViralWhatsApp
{
    [Key]
    public Guid EncuestaId { get; set; }

    public bool FueCompartido { get; set; }

    public DateTime? FechaCompartido { get; set; }

    /// <summary>
    /// Código corto del enlace compartido.
    ///
    /// Desviación documentada del plan técnico: su tabla solo tiene
    /// (<c>encuesta_id</c>, <c>fue_compartido</c>, <c>fecha_compartido</c>) y la US-004.3 pide un
    /// enlace **corto** con identificador único. Con el GUID de la encuesta el enlace funciona
    /// pero no es corto, y además expondría un identificador interno en un mensaje que se
    /// reenvía. Ocho caracteres de un alfabeto sin letras ambiguas alcanzan y se pueden
    /// registrar aparte.
    /// </summary>
    [Required]
    [MaxLength(8)]
    public string CodigoCompartido { get; set; } = string.Empty;
}

/// <summary>
/// Proyección local de los restaurantes de Identity (read-model).
///
/// Se alimenta consumiendo <c>RestauranteRegistrado</c>. El Nivel 3 lo necesita para
/// nombrar al restaurante en el mensaje de WhatsApp (US-004.3): sin la réplica, el
/// mensaje viral no puede decir en qué restaurante comió el cliente.
/// </summary>
[Table("restaurantes_replicados")]
public class RestauranteReplicado
{
    [Key]
    public Guid Id { get; set; } // Mismo Id que en Identity

    [Required]
    [MaxLength(255)]
    public string NombreComercial { get; set; } = string.Empty;

    /// <summary>
    /// Ciudad de la sede. La pantalla de calificaciones muestra la zona del restaurante y
    /// filtra por ella, y el evento <c>RestauranteRegistrado</c> ya la trae: sin guardarla,
    /// la réplica no puede resolver el nombre y la zona de una fila.
    /// </summary>
    [MaxLength(100)]
    public string Ciudad { get; set; } = string.Empty;

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
