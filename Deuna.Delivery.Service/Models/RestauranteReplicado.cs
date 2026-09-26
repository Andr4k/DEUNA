using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Delivery.Service.Models;

/// <summary>
/// Proyección local (read-model) de los restaurantes registrados en Identity.
/// Se alimenta consumiendo <c>RestauranteRegistrado</c>.
///
/// Guarda la dirección de la sede y no coordenadas, a propósito: los restaurantes son
/// fijos y se ubican por su dirección. El mapa la resuelve con el geocodificador —una vez,
/// y el resultado queda cacheado—, así que la posición no se congela acá: si la sede se
/// muda, se corrige la dirección y el mapa la sigue.
/// </summary>
[Table("restaurantes_replicados")]
public class RestauranteReplicado
{
    [Key]
    public Guid Id { get; set; } // Mismo Id que en Identity

    [Required]
    [MaxLength(200)]
    public string NombreComercial { get; set; } = string.Empty;

    /// <summary>Dirección de la sede tal como la registró el restaurante: texto, no coordenadas.</summary>
    [Required]
    [MaxLength(500)]
    public string DireccionSede { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Ciudad { get; set; } = string.Empty;

    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
