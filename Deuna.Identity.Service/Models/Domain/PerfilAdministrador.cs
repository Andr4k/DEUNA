using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Perfil de Administrador - permisos y auditoría
/// </summary>
[Table("perfiles_administrador")]
public class PerfilAdministrador
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UsuarioId { get; set; }

    [MaxLength(100)]
    public string? Cargo { get; set; } // SUPER_ADMIN, ADMIN, OPERADOR, SOPORTE

    [MaxLength(200)]
    public string? Departamento { get; set; }

    [MaxLength(100)]
    public string? JefeDirectoId { get; set; } // Guid del usuario jefe

    public bool EsSuperAdmin { get; set; } = false;
    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? UltimoAcceso { get; set; }

    // Navegación
    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }

    // Subtablas 1:N
    public List<PermisoAdministrador> Permisos { get; set; } = new();
    public List<AuditoriaAdministrador> Auditorias { get; set; } = new();
}

/// <summary>
/// Permisos específicos del administrador (subtabla)
/// </summary>
[Table("permisos_administrador")]
public class PermisoAdministrador
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilAdministradorId { get; set; }

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

    [ForeignKey(nameof(PerfilAdministradorId))]
    public PerfilAdministrador? PerfilAdministrador { get; set; }
}

/// <summary>
/// Auditoría de acciones del administrador (subtabla)
/// </summary>
[Table("auditoria_administrador")]
public class AuditoriaAdministrador
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilAdministradorId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Accion { get; set; } = string.Empty; // CREATE_USER, UPDATE_ROLE, DELETE_RESTAURANT, etc.

    [MaxLength(100)]
    public string? EntidadAfectada { get; set; } // Usuario, Restaurante, Pedido, etc.

    public Guid? EntidadId { get; set; }

    [MaxLength(2000)]
    public string? Detalle { get; set; } // JSON con antes/después

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public bool Exitoso { get; set; } = true;

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PerfilAdministradorId))]
    public PerfilAdministrador? PerfilAdministrador { get; set; }
}