using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Feedback.Service.Models;

/// <summary>
/// Encuesta de feedback Nivel 1 (US-004.1). Esquema del plan técnico:
/// tabla feedback_encuestas, una fila por pedido (UNIQUE).
/// </summary>
[Table("feedback_encuestas")]
public class FeedbackEncuesta
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Pedido evaluado. UNIQUE: un pedido solo puede tener un feedback Nivel 1.</summary>
    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    public Guid RestauranteId { get; set; }

    /// <summary>
    /// Repartidor evaluado. Nullable: el plan lo declara NOT NULL, pero la asignación
    /// llega por evento (US-003.1, sin tarea asignada todavía); exigirlo haría imposible
    /// registrar feedback de pedidos cuya asignación no fue proyectada.
    /// </summary>
    public Guid? RepartidorId { get; set; }

    [Range(1, 5)]
    public int RatingGeneralComida { get; set; }

    [Range(1, 5)]
    public int RatingServicioRepartidor { get; set; }

    public bool DeseaRecomendar { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Proyección local (read-model) de los pedidos de Orders Service.
/// Necesaria para validar que el pedido existe y está en estado <c>Entregado</c>
/// antes de aceptar feedback, sin acoplamiento síncrono entre servicios (ADR-005).
/// </summary>
[Table("pedidos_replicados")]
public class PedidoReplicado
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identificador del pedido en Orders Service (único, garantiza idempotencia).</summary>
    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Codigo { get; set; } = string.Empty;

    [Required]
    public Guid ClienteId { get; set; }

    [Required]
    public Guid RestauranteId { get; set; }

    public Guid? RepartidorId { get; set; }

    /// <summary>Pendiente | Buscando | Asignado | EnLocal | EnRuta | Entregado | Cancelado.</summary>
    [Required]
    [MaxLength(50)]
    public string Estado { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? ActualizadoEn { get; set; }

    /// <summary>CorrelationId del último mensaje aplicado (trazabilidad).</summary>
    public Guid? CorrelationId { get; set; }

    public const string EstadoPendiente = "Pendiente";
    public const string EstadoEntregado = "Entregado";
    public const string EstadoCancelado = "Cancelado";
}

/// <summary>
/// Proyección local (read-model) de los repartidores registrados en Identity.
/// Se alimenta consumiendo <c>RepartidorRegistrado</c>.
///
/// La encuesta guarda a qué repartidor se califica (US-004.1); sin esta réplica el
/// identificador llega sin perfil detrás y el restaurante no puede saber a quién
/// corresponde la calificación.
/// </summary>
[Table("repartidores_replicados")]
public class RepartidorReplicado
{
    [Key]
    public Guid Id { get; set; } // Mismo Id que en Identity

    [Required]
    [MaxLength(200)]
    public string NombreCompleto { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DocumentoIdentidad { get; set; }

    [MaxLength(100)]
    public string? CiudadOperacion { get; set; }

    [MaxLength(500)]
    public string? FotoPerfilUrl { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
