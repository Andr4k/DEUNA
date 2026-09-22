using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Perfil de Restaurante - información comercial y operativa
/// </summary>
[Table("perfiles_restaurante")]
public class PerfilRestaurante
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UsuarioId { get; set; }

    [Required]
    [MaxLength(200)]
    public string NombreComercial { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string RazonSocial { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Nit { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string DireccionSede { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Ciudad { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(10,8)")]
    public decimal Latitud { get; set; }

    [Required]
    [Column(TypeName = "decimal(11,8)")]
    public decimal Longitud { get; set; }

    [MaxLength(100)]
    public string? Distrito { get; set; }

    [MaxLength(100)]
    public string? Provincia { get; set; }

    [MaxLength(100)]
    public string? Departamento { get; set; }

    [MaxLength(10)]
    public string? CodigoPostal { get; set; }

    [MaxLength(20)]
    public string? Telefono { get; set; }

    [MaxLength(200)]
    public string? EmailContacto { get; set; }

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    public bool AceptaPedidos { get; set; } = true;
    public bool Activo { get; set; } = true;

    public TimeSpan HoraApertura { get; set; } = new(8, 0, 0);
    public TimeSpan HoraCierre { get; set; } = new(22, 0, 0);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navegación
    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }

    // Subtablas 1:N
    public List<HorarioAtencion> HorariosAtencion { get; set; } = new();
    public List<ZonaCoberturaRestaurante> ZonasCobertura { get; set; } = new();
    public List<MetodoPagoRestaurante> MetodosPago { get; set; } = new();
}

/// <summary>
/// Horarios de atención del restaurante (subtabla)
/// </summary>
[Table("horarios_atencion")]
public class HorarioAtencion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRestauranteId { get; set; }

    [Required]
    [MaxLength(20)]
    public string DiaSemana { get; set; } = string.Empty; // Lunes, Martes, etc.

    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }

    public bool Cerrado { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PerfilRestauranteId))]
    public PerfilRestaurante? PerfilRestaurante { get; set; }
}

/// <summary>
/// Zonas de cobertura de entrega del restaurante (subtabla)
/// </summary>
[Table("zonas_cobertura_restaurante")]
public class ZonaCoberturaRestaurante
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRestauranteId { get; set; }

    [Required]
    [MaxLength(100)]
    public string NombreZona { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Descripcion { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal CostoEnvio { get; set; } = 0;

    [Column(TypeName = "decimal(10,2)")]
    public decimal PedidoMinimo { get; set; } = 0;

    public int TiempoEstimadoMinutos { get; set; } = 30;

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PerfilRestauranteId))]
    public PerfilRestaurante? PerfilRestaurante { get; set; }
}

/// <summary>
/// Métodos de pago aceptados por el restaurante (subtabla)
/// </summary>
[Table("metodos_pago_restaurante")]
public class MetodoPagoRestaurante
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRestauranteId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Tipo { get; set; } = string.Empty; // EFECTIVO, TARJETA, YAPE, PLIN, TRANSFERENCIA

    [MaxLength(100)]
    public string? Proveedor { get; set; } // VISA, MASTERCARD, BCP, INTERBANK, etc.

    public bool Activo { get; set; } = true;
    public bool EsPredeterminado { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PerfilRestauranteId))]
    public PerfilRestaurante? PerfilRestaurante { get; set; }
}