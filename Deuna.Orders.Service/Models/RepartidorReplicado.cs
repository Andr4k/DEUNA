using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Orders.Service.Models;

/// <summary>
/// Proyección local (read-model) de los domiciliarios registrados en Identity.
/// Se alimenta consumiendo <c>RepartidorRegistrado</c>.
///
/// El historial de asignaciones (<see cref="AsignacionReplicada"/>) guarda el
/// <c>RepartidorId</c> y los tiempos del ciclo, pero no quién es el domiciliario: el
/// evento <c>PedidoAsignado</c> no lleva su nombre. Sin esta réplica, la pantalla de
/// servicios finalizados tendría que pedirle los nombres a Delivery en cada lectura, que
/// es justo el acoplamiento que la réplica evita: cada servicio proyecta lo que necesita.
///
/// No guarda la calificación: llega con el evento de calificación registrada (su propia
/// rebanada) y el contrato de la pantalla ya la declara como <c>null</c> mientras no exista.
/// </summary>
[Table("repartidores_replicados")]
public class RepartidorReplicado
{
    [Key]
    public Guid Id { get; set; } // Mismo Id que en Identity

    [Required]
    [MaxLength(200)]
    public string NombreCompleto { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DocumentoIdentidad { get; set; }

    [MaxLength(100)]
    public string? CiudadOperacion { get; set; }

    [MaxLength(500)]
    public string? FotoPerfilUrl { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
