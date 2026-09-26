using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Orders.Service.Models;

/// <summary>
/// Historial de asignaciones de un pedido (subtabla 1:N del diseño).
///
/// Orders lo mantiene para poder mostrar el ciclo completo del pedido —quién lo llevó y
/// cuándo pasó por cada hito— sin tener que consultar a Delivery. Es 1:N a propósito: un
/// pedido puede pasar por varios domiciliarios si el asignado rechaza antes de llegar al
/// local, y cada intento tiene que quedar registrado.
///
/// Se alimenta de los eventos del ciclo de entrega: <c>PedidoAsignado</c>,
/// <c>PedidoActualizado</c> y <c>PedidoEntregado</c>.
/// </summary>
[Table("pedidos_asignados_repartidor")]
public class AsignacionReplicada
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    public Guid RepartidorId { get; set; }

    /// <summary>Momento en que el sistema le asignó el pedido.</summary>
    public DateTime FechaAsignacion { get; set; } = DateTime.UtcNow;

    /// <summary>Momento en que escaneó el QR del restaurante.</summary>
    public DateTime? FechaLlegadaLocal { get; set; }

    /// <summary>Momento en que salió del local con el pedido.</summary>
    public DateTime? FechaSalidaRuta { get; set; }

    /// <summary>Momento en que el cliente escaneó el QR de cierre.</summary>
    public DateTime? FechaEntrega { get; set; }

    /// <summary>Asignado | EnLocal | EnRuta | Completado | Rechazado.</summary>
    [Required]
    [MaxLength(30)]
    public string EstadoAsignacion { get; set; } = EstadoAsignado;

    [MaxLength(300)]
    public string? MotivoRechazo { get; set; }

    public const string EstadoAsignado = "Asignado";
    public const string EstadoEnLocal = "EnLocal";
    public const string EstadoEnRuta = "EnRuta";
    public const string EstadoCompletado = "Completado";
    public const string EstadoRechazado = "Rechazado";
}
