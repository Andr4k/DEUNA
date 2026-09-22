using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Perfil de Repartidor - información de entrega y documentos
/// </summary>
[Table("perfiles_repartidor")]
public class PerfilRepartidor
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UsuarioId { get; set; }

    [Required]
    [MaxLength(20)]
    public string NumeroLicencia { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? TipoLicencia { get; set; } // A1, A2, B, etc.

    public DateTime? FechaVencimientoLicencia { get; set; }

    [Required]
    [MaxLength(255)]
    public string NombreCompleto { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string CiudadOperacion { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? FotoPerfilUrl { get; set; }

    public bool Disponible { get; set; } = true;
    public bool Activo { get; set; } = true;

    [Column(TypeName = "decimal(10,2)")]
    public decimal CalificacionPromedio { get; set; } = 0;

    public int TotalEntregas { get; set; } = 0;
    public int EntregasCompletadas { get; set; } = 0;
    public int EntregasCanceladas { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navegación
    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }

    // Subtablas 1:N
    public List<VehiculoRepartidor> Vehiculos { get; set; } = new();
    public List<ZonaCoberturaRepartidor> ZonasCobertura { get; set; } = new();
    public List<DocumentoRepartidor> Documentos { get; set; } = new();
}

/// <summary>
/// Vehículos del repartidor (subtabla)
/// </summary>
[Table("vehiculos_repartidor")]
public class VehiculoRepartidor
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRepartidorId { get; set; }

    [Required]
    public TipoVehiculo Tipo { get; set; }

    [MaxLength(50)]
    public string? Marca { get; set; }

    [MaxLength(50)]
    public string? Modelo { get; set; }

    [MaxLength(20)]
    public string? Placa { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    public int? AnioFabricacion { get; set; }

    [MaxLength(100)]
    public string? NumeroSOAT { get; set; }

    public DateTime? FechaVencimientoSOAT { get; set; }

    [MaxLength(100)]
    public string? NumeroTarjetaPropiedad { get; set; }

    public EstadoVehiculo Estado { get; set; } = EstadoVehiculo.ACTIVO;

    public bool EsPrincipal { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PerfilRepartidorId))]
    public PerfilRepartidor? PerfilRepartidor { get; set; }
}

/// <summary>
/// Zonas de cobertura del repartidor (subtabla)
/// </summary>
[Table("zonas_cobertura_repartidor")]
public class ZonaCoberturaRepartidor
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRepartidorId { get; set; }

    [Required]
    [MaxLength(100)]
    public string NombreZona { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PerfilRepartidorId))]
    public PerfilRepartidor? PerfilRepartidor { get; set; }
}

/// <summary>
/// Documentos del repartidor (subtabla)
/// </summary>
[Table("documentos_repartidor")]
public class DocumentoRepartidor
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PerfilRepartidorId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Tipo { get; set; } = string.Empty; // LICENCIA, SOAT, TARJETA_PROPIEDAD, DNI, ANTECEDENTES

    [MaxLength(500)]
    public string? UrlArchivo { get; set; }

    [MaxLength(200)]
    public string? NumeroDocumento { get; set; }

    public DateTime? FechaEmision { get; set; }
    public DateTime? FechaVencimiento { get; set; }

    public EstadoDocumento Estado { get; set; } = EstadoDocumento.PENDIENTE;

    [MaxLength(500)]
    public string? Observaciones { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PerfilRepartidorId))]
    public PerfilRepartidor? PerfilRepartidor { get; set; }
}