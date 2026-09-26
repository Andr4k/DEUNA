using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Catálogo de lo que expone el sistema, por servicio (tabla nueva).
///
/// Un recurso es una cosa que se pide por endpoint (los "usuarios", los "pedidos",
/// las "calificaciones"); `Servicio` dice cuál de los servicios la expone. Sin este
/// catálogo los permisos no tienen contra qué definirse.
///
/// `Clave` es el nombre con el que los permisos apuntan acá: es lo que
/// `PermisoUsuario.Recurso` guarda como texto. Se queda como texto y no como FK a
/// propósito, para que la tabla de permisos tenga el mismo formato que
/// `permisos_administrador`, que es el molde que se copia.
/// </summary>
[Table("recursos")]
public class Recurso
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Servicio { get; set; } = string.Empty; // identity, orders, delivery, feedback, metrics

    [Required]
    [MaxLength(100)]
    public string Clave { get; set; } = string.Empty; // Ej: "usuarios", "pedidos"

    [Required]
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [MaxLength(200)]
    public string? Acciones { get; set; } // Ej: "READ,UPDATE" - las acciones que el recurso expone

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
