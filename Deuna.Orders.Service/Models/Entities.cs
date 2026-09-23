using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Deuna.Orders.Service.Models;

/// <summary>
/// Pedido principal - tabla maestra
/// </summary>
[Table("pedidos")]
public class Pedido
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ClienteId { get; set; }

    [Required]
    public Guid RestauranteId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Codigo { get; set; } = string.Empty; // PED-XXXXXX

    [Required]
    [MaxLength(50)]
    public string Estado { get; set; } = "Pendiente"; // Pendiente, Confirmado, Preparando, Listo, EnCamino, Entregado, Cancelado

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal CostoEnvio { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [Required]
    public Guid DireccionEntregaId { get; set; }

    [Required]
    [MaxLength(36)]
    public string TokenQrLocal { get; set; } = string.Empty; // UUID v4: lo muestra el restaurante

    [Required]
    [MaxLength(36)]
    public string TokenQrEntrega { get; set; } = string.Empty; // UUID v4: lo muestra el domiciliario

    [MaxLength(500)]
    public string? NotasCliente { get; set; }

    [MaxLength(500)]
    public string? NotasRestaurante { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaConfirmacion { get; set; }
    public DateTime? FechaPreparacion { get; set; }
    public DateTime? FechaListo { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public DateTime? FechaCancelacion { get; set; }

    // Navegación
    public virtual DireccionEntrega DireccionEntrega { get; set; } = null!;
    public virtual RestauranteReplicado Restaurante { get; set; } = null!;
    public virtual ICollection<ItemPedido> Items { get; set; } = new List<ItemPedido>();
    public virtual ICollection<ContactoPedido> Contactos { get; set; } = new List<ContactoPedido>();
    public virtual TarifaAplicada? Tarifa { get; set; }
}

/// <summary>
/// Item del pedido (subtabla 1:N)
/// </summary>
[Table("items_pedido")]
public class ItemPedido
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    [MaxLength(200)]
    public string NombreProducto { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [Required]
    public int Cantidad { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioUnitario { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    // Navegación
    [ForeignKey(nameof(PedidoId))]
    public virtual Pedido Pedido { get; set; } = null!;
}

/// <summary>
/// Dirección de entrega con geografía PostGIS
/// </summary>
[Table("direcciones_entrega")]
public class DireccionEntrega
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Calle { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Numero { get; set; }

    [MaxLength(100)]
    public string? Interior { get; set; }

    [MaxLength(100)]
    public string? Referencia { get; set; }

    [Required]
    [MaxLength(100)]
    public string Ciudad { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Departamento { get; set; }

    [MaxLength(20)]
    public string? CodigoPostal { get; set; }

    [Required]
    [Column(TypeName = "geography (point)")]
    public Point Ubicacion { get; set; } = null!;

    // Navegación
    public virtual ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
}

/// <summary>
/// Contactos del pedido (subtabla 1:N)
/// </summary>
[Table("contactos_pedido")]
public class ContactoPedido
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Tipo { get; set; } = string.Empty; // Cliente, Restaurante, Repartidor

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Telefono { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Email { get; set; }

    // Navegación
    [ForeignKey(nameof(PedidoId))]
    public virtual Pedido Pedido { get; set; } = null!;
}

/// <summary>
/// Tarifa aplicada al pedido (snapshot inmutable)
/// </summary>
[Table("tarifas_aplicadas")]
public class TarifaAplicada
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TipoTarifa { get; set; } = string.Empty; // Distancia, Zona, Fija

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal CostoBase { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostoPorKm { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? DistanciaKm { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostoAdicional { get; set; }

    [MaxLength(200)]
    public string? DetalleCalculo { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalCalculado { get; set; }

    // Navegación
    [ForeignKey(nameof(PedidoId))]
    public virtual Pedido Pedido { get; set; } = null!;
}

/// <summary>
/// Réplica de restaurante para Orders Service (denormalizada)
/// Se siembra desde eventos de Identity si Identity no está disponible
/// </summary>
[Table("restaurantes_replicados")]
public class RestauranteReplicado
{
    [Key]
    public Guid Id { get; set; } // Mismo Id que en Identity

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

    [Required]
    [Column(TypeName = "geography (point)")]
    public Point Ubicacion { get; set; } = null!;

    [MaxLength(100)]
    public string? Distrito { get; set; }

    [MaxLength(100)]
    public string? Provincia { get; set; }

    [MaxLength(100)]
    public string? Departamento { get; set; }

    [Required]
    [Column(TypeName = "decimal(10,2)")]
    public decimal RadioCoberturaKm { get; set; } = 5.0m;

    public bool AceptaPedidos { get; set; } = true;
    public bool Activo { get; set; } = true;

    public TimeSpan HoraApertura { get; set; } = new(8, 0, 0);
    public TimeSpan HoraCierre { get; set; } = new(22, 0, 0);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Subtablas 1:N replicadas
    public virtual ICollection<ZonaCoberturaReplicada> ZonasCobertura { get; set; } = new List<ZonaCoberturaReplicada>();
    public virtual ICollection<HorarioAtencionReplicado> HorariosAtencion { get; set; } = new List<HorarioAtencionReplicado>();
}

/// <summary>
/// Zona de cobertura replicada
/// </summary>
[Table("zonas_cobertura_replicadas")]
public class ZonaCoberturaReplicada
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RestauranteReplicadoId { get; set; }

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

    // Geografía opcional para zona poligonal
    [Column(TypeName = "geography (polygon)")]
    public Polygon? Geom { get; set; }

    // Navegación
    [ForeignKey(nameof(RestauranteReplicadoId))]
    public virtual RestauranteReplicado RestauranteReplicado { get; set; } = null!;
}

/// <summary>
/// Horario de atención replicado
/// </summary>
[Table("horarios_atencion_replicados")]
public class HorarioAtencionReplicado
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid RestauranteReplicadoId { get; set; }

    [Required]
    [MaxLength(20)]
    public string DiaSemana { get; set; } = string.Empty;

    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }

    public bool Cerrado { get; set; } = false;

    // Navegación
    [ForeignKey(nameof(RestauranteReplicadoId))]
    public virtual RestauranteReplicado RestauranteReplicado { get; set; } = null!;
}