namespace Deuna.Notification.Service.Services;

/// <summary>
/// Credenciales de Firebase Cloud Messaging (HTTP v1).
///
/// Sin <see cref="ProjectId"/> y <see cref="ServiceAccountJson"/> el servicio no intenta
/// hablar con FCM: usa el emisor de log. Así el pipeline se puede correr en desarrollo y
/// en tests sin credenciales, y activar el envío real es solo cuestión de configuración.
/// </summary>
public class FcmOptions
{
    public const string Seccion = "Fcm";

    public string? ProjectId { get; set; }

    /// <summary>Contenido del JSON de la cuenta de servicio (no la ruta).</summary>
    public string? ServiceAccountJson { get; set; }

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
