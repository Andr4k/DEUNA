using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Identity.Service.Models.Domain;

/// <summary>
/// Código de autorización que habilita el alta de un restaurante.
///
/// Lo emite el área comercial fuera del sistema, después del contacto previo con el
/// restaurante, y es obligatorio para registrarse (FR-001.8 a FR-001.10). Es de un solo
/// uso: un código habilita una única alta.
/// </summary>
[Table("codigos_autorizacion")]
public class CodigoAutorizacion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Código que se entrega al restaurante. Único en la tabla.</summary>
    [Required]
    [MaxLength(32)]
    public string Codigo { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="EstadoCodigoAutorizacion.Disponible"/> mientras no se haya validado,
    /// <see cref="EstadoCodigoAutorizacion.Usado"/> una vez validado.
    /// <see cref="EstadoCodigoAutorizacion.Vencido"/> es derivado y no se persiste.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Estado { get; set; } = EstadoCodigoAutorizacion.Disponible;

    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

    public DateTime FechaVencimiento { get; set; }

    /// <summary>Momento en que el restaurante validó el código.</summary>
    public DateTime? FechaUso { get; set; }

    /// <summary>Auditoría: quién del área comercial emitió el código.</summary>
    public Guid? EmitidoPorUsuarioId { get; set; }

    /// <summary>Alta asociada: el restaurante que completó el registro con este código.</summary>
    public Guid? RestauranteId { get; set; }

    /// <summary>Referencia interna de la emisión (por ejemplo, a qué restaurante se destinó).</summary>
    [MaxLength(200)]
    public string? Notas { get; set; }

    /// <summary>Un código disponible cuya fecha de vencimiento ya pasó.</summary>
    public bool EstaVencido(DateTime ahora) =>
        Estado == EstadoCodigoAutorizacion.Disponible && FechaVencimiento <= ahora;
}

public static class EstadoCodigoAutorizacion
{
    public const string Disponible = "Disponible";
    public const string Usado = "Usado";

    /// <summary>Estado derivado: no se persiste, se calcula por vencimiento.</summary>
    public const string Vencido = "Vencido";
}
