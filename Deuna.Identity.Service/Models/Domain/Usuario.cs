using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Usuario base del sistema - tabla principal de autenticación
/// </summary>
[Table("usuarios")]
public class Usuario
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; } = false;
    public bool PhoneConfirmed { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Relaciones 1:1 con perfiles
    public PerfilRestaurante? PerfilRestaurante { get; set; }
    public PerfilRepartidor? PerfilRepartidor { get; set; }
    public PerfilAdministrador? PerfilAdministrador { get; set; }

    // Refresh tokens
    public List<RefreshToken> RefreshTokens { get; set; } = new();
}

public enum TipoDocumentoIdentidad
{
    DNI = 1,
    CE = 2,      // Carné de Extranjería
    PASAPORTE = 3,
    RUC = 4
}

public enum TipoVehiculo
{
    MOTOCICLETA = 1,
    BICICLETA = 2,
    AUTO = 3,
    CAMIONETA = 4
}

public enum EstadoVehiculo
{
    ACTIVO = 1,
    MANTENIMIENTO = 2,
    BAJA = 3
}

public enum EstadoDocumento
{
    PENDIENTE = 1,
    APROBADO = 2,
    RECHAZADO = 3,
    EXPIRADO = 4
}

public enum TipoPermiso
{
    GESTION_USUARIOS = 1,
    GESTION_ROLES = 2,
    GESTION_RESTAURANTES = 3,
    GESTION_REPARTIDORES = 4,
    GESTION_PEDIDOS = 5,
    GESTION_PAGOS = 6,
    REPORTES = 7,
    CONFIGURACION = 8,
    AUDITORIA = 9
}