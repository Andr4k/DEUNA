using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Delivery.Service.Models;

/// <summary>
/// Proyección local (read-model) de los repartidores registrados en Identity.
/// Se alimenta consumiendo <c>RepartidorRegistrado</c>.
///
/// Sin esta réplica la asignación automática no sabe qué domiciliarios existen: el
/// matching por cercanía devuelve identificadores desde Redis, pero no hay perfil
/// detrás para notificar ni para mostrar en la app.
///
/// No guarda disponibilidad a propósito: la disponibilidad se deriva de la telemetría
/// vigente y de no tener una entrega activa (TASK-302). Un flag aquí sería una segunda
/// fuente de verdad que queda desactualizada en cuanto el repartidor cierra la app.
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
