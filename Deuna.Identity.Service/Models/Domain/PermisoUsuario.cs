using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Permisos de un usuario del portal (subtabla), por recurso y acción.
///
/// Es el mismo formato que <see cref="PermisoAdministrador"/> con `UsuarioId` en
/// lugar de `PerfilAdministradorId`: apunta al usuario directo, porque no existe un
/// `perfiles_usuario` genérico sino un perfil por tipo. La tabla del molde
/// (`permisos_administrador`) se deja como está.
///
/// Las "acciones específicas" del portal son las filas de acá: el endpoint resuelve
/// su recurso + acción y consulta el permiso del usuario antes de ejecutar. El nivel
/// de acceso que se elige en la pantalla NO se guarda: es un preset que aplica un
/// conjunto de permisos, y lo que vale es lo que quede en esta tabla.
/// </summary>
[Table("permisos_usuarios")]
public class PermisoUsuario
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UsuarioId { get; set; }

    [Required]
    public TipoPermiso Permiso { get; set; }

    [MaxLength(100)]
    public string? Recurso { get; set; } // Ej: "restaurantes", "pedidos", "usuarios"

    [MaxLength(50)]
    public string? Accion { get; set; } // CREATE, READ, UPDATE, DELETE, APPROVE, REJECT

    public bool Concedido { get; set; } = true;

    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }

    [MaxLength(500)]
    public string? Observaciones { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }
}
