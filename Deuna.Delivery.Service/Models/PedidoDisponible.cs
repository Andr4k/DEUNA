using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Delivery.Service.Models;

/// <summary>
/// Proyección local (read-model) de los pedidos creados por Orders Service.
/// Se alimenta consumiendo el evento PedidoCreado; un pedido nuevo entra con
/// estado "Buscando" y queda disponible para que un repartidor lo acepte.
/// </summary>
[Table("pedidos_disponibles")]
public class PedidoDisponible
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

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    /// <summary>Buscando | Asignado | Recogido | Entregado | Cancelado.</summary>
    [Required]
    [MaxLength(50)]
    public string Estado { get; set; } = EstadoBuscando;

    /// <summary>Token QR que el repartidor debe validar al entregar.</summary>
    [Required]
    [MaxLength(64)]
    public string QrCodigo { get; set; } = string.Empty;

    /// <summary>CorrelationId del mensaje que originó el registro (trazabilidad).</summary>
    public Guid? CorrelationId { get; set; }

    public Guid? RepartidorId { get; set; }
    public DateTime? AsignadoAt { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public const string EstadoBuscando = "Buscando";
    public const string EstadoAsignado = "Asignado";
}
