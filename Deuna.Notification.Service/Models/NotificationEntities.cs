using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Deuna.Notification.Service.Models;

/// <summary>
/// Dispositivo al que se le pueden enviar notificaciones push.
///
/// El token lo emite FCM y es del DISPOSITIVO, no del usuario: si otra persona inicia
/// sesión en el mismo teléfono, el token se reasigna. Por eso el token es único en la
/// tabla y no la pareja (usuario, token).
/// </summary>
[Table("dispositivos")]
public class Dispositivo
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UsuarioId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    /// <summary>android | ios | web.</summary>
    [Required]
    [MaxLength(20)]
    public string Plataforma { get; set; } = string.Empty;

    /// <summary>
    /// Un token rechazado por el proveedor se desactiva en lugar de borrarse: deja el
    /// rastro de que ese dispositivo existió y evita seguir intentando contra un token muerto.
    /// </summary>
    public bool Activo { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Última vez que el dispositivo recibió una notificación.</summary>
    public DateTime? UltimoUsoAt { get; set; }

    public const string PlataformaAndroid = "android";
    public const string PlataformaIos = "ios";
}

/// <summary>
/// Registro de cada intento de notificación, para poder responder "por qué este domiciliario
/// no se enteró del pedido". No es una cola de salida: es trazabilidad.
/// </summary>
[Table("notificaciones_enviadas")]
public class NotificacionEnviada
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Id del mensaje de MassTransit que originó la notificación. UNIQUE: si el broker
    /// reentrega el evento, no se notifica dos veces al mismo usuario.
    /// </summary>
    public Guid? MensajeId { get; set; }

    [Required]
    [MaxLength(50)]
    public string TipoEvento { get; set; } = string.Empty;

    [Required]
    public Guid DestinatarioId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Titulo { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Cuerpo { get; set; }

    /// <summary>Enviada | Fallida | SinDispositivo.</summary>
    [Required]
    [MaxLength(30)]
    public string Estado { get; set; } = EstadoSinDispositivo;

    [MaxLength(50)]
    public string? Proveedor { get; set; }

    /// <summary>Cantidad de dispositivos a los que se intentó enviar.</summary>
    public int Dispositivos { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EnviadoAt { get; set; }

    public const string EstadoEnviada = "Enviada";
    public const string EstadoFallida = "Fallida";
    public const string EstadoSinDispositivo = "SinDispositivo";
}
