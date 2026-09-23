using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Delivery.Service.Models;

/// <summary>
/// Historial de asignaciones de un pedido a un repartidor (TASK-303).
///
/// Es 1:N a propósito: un pedido puede pasar por varios repartidores si el asignado
/// rechaza antes de llegar al local, y el rechazo tiene que quedar registrado. El estado
/// actual del pedido vive en <see cref="PedidoDisponible"/>; esta tabla es la trazabilidad.
/// </summary>
[Table("asignaciones_repartidor")]
public class AsignacionRepartidor
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [Required]
    public Guid RepartidorId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Estado { get; set; } = EstadoAsignado;

    /// <summary>Distancia del repartidor al punto de entrega en el momento de asignarlo.</summary>
    public double? DistanciaMetros { get; set; }

    public DateTime FechaAsignacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Momento en que el domiciliario escaneó el QR del local y se confirmó su presencia
    /// física (TASK-304). Vive por asignación: cada intento tiene su propia llegada.
    /// </summary>
    public DateTime? FechaLlegadaLocal { get; set; }

    public DateTime? FechaRechazo { get; set; }

    [MaxLength(300)]
    public string? MotivoRechazo { get; set; }

    public const string EstadoAsignado = "Asignado";
    public const string EstadoConfirmadoEnLocal = "ConfirmadoEnLocal";
    public const string EstadoRechazado = "Rechazado";
}
