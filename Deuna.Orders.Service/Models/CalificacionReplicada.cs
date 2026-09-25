using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Orders.Service.Models;

/// <summary>
/// Calificación de un pedido entregado, con los dos sujetos que muestra el historial de
/// servicios finalizados: el domiciliario y el restaurante.
///
/// Se alimenta consumiendo <c>CalificacionRegistrada</c>. Vive en su propia tabla, 1:1 con
/// el pedido, y no como columnas de <see cref="AsignacionReplicada"/> por dos razones: la
/// asignación es 1:N (un pedido puede pasar por varios domiciliarios y la calificación es
/// del pedido, no de cada intento), y la nota del restaurante tiene que poder guardarse
/// aunque el pedido no tenga ninguna asignación proyectada.
///
/// Los dos puntajes van en columnas separadas y no en una sola: la encuesta califica
/// sujetos distintos y cruzarlos es peor que no tenerlos.
/// </summary>
[Table("calificaciones_replicadas")]
public class CalificacionReplicada
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Pedido calificado. UNIQUE: la encuesta es una por pedido.</summary>
    [Required]
    public Guid PedidoId { get; set; }

    /// <summary>
    /// Domiciliario que entregó. Nullable: la encuesta no exige que la asignación haya
    /// sido proyectada, y el cliente califica el servicio aunque no se sepa quién lo hizo.
    /// </summary>
    public Guid? RepartidorId { get; set; }

    /// <summary>Calificación del domiciliario (<c>RatingServicioRepartidor</c>), 1..5.</summary>
    [Range(1, 5)]
    public int CalificacionDomiciliario { get; set; }

    /// <summary>Calificación del restaurante (<c>RatingGeneralComida</c>), 1..5.</summary>
    [Range(1, 5)]
    public int CalificacionRestaurante { get; set; }

    /// <summary>Momento en que el cliente registró la calificación.</summary>
    public DateTime FechaCalificacion { get; set; } = DateTime.UtcNow;
}
