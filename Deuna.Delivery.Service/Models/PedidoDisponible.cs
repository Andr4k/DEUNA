using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Deuna.Shared.Domain;

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

    /// <summary>
    /// Token que el domiciliario escanea al llegar al local. Está en la proyección para poder
    /// compararlo, pero NO se le entrega al domiciliario: lo tiene que escanear del QR del
    /// restaurante. Dárselo sería permitirle confirmar la llegada sin haber ido.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string TokenQrLocal { get; set; } = string.Empty;

    /// <summary>
    /// Token que el domiciliario muestra para que el cliente lo escanee al recibir (TASK-305).
    /// Este sí se le entrega: sin él no puede mostrarlo.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string TokenQrEntrega { get; set; } = string.Empty;

    /// <summary>CorrelationId del mensaje que originó el registro (trazabilidad).</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// Punto de entrega del pedido (WGS84). Origen de las consultas geoespaciales de
    /// tracking: sin él no se puede ubicar al repartidor respecto del destino.
    /// </summary>
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }

    public Guid? RepartidorId { get; set; }
    public DateTime? AsignadoAt { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // Los literales viven en Deuna.Shared: los estados viajan como texto dentro de los
    // eventos, así que tienen que ser idénticos en todos los servicios.
    public const string EstadoBuscando = EstadosPedido.Buscando;
    public const string EstadoAsignado = EstadosPedido.Asignado;
    public const string EstadoConfirmadoEnLocal = EstadosPedido.ConfirmadoEnLocal;
    public const string EstadoEnRuta = EstadosPedido.EnRuta;
    public const string EstadoEntregado = EstadosPedido.Entregado;
    public const string EstadoCancelado = EstadosPedido.Cancelado;

    /// <summary>
    /// Estados en los que el repartidor está ocupado con este pedido. La asignación
    /// automática descarta a quien tenga alguno de estos: un repartidor no puede llevar
    /// dos entregas a la vez (FR-003.1).
    /// </summary>
    public static readonly string[] EstadosQueOcupanAlRepartidor =
        [EstadoAsignado, EstadoConfirmadoEnLocal, EstadoEnRuta];
}
