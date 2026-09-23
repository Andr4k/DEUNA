namespace Deuna.Notification.Service.Services;

/// <summary>Contenido de una notificación push.</summary>
public record MensajePush(
    string Titulo,
    string Cuerpo,
    IReadOnlyDictionary<string, string> Datos);

/// <summary>Resultado del intento de envío contra el proveedor.</summary>
public record ResultadoPush(
    bool Exito,
    string Proveedor,
    string? Error = null,
    /// <summary>El proveedor dice que el token ya no existe: el dispositivo quedó muerto.</summary>
    bool TokenInvalido = false);

/// <summary>
/// Proveedor de notificaciones push. Abstraerlo permite probar la lógica del servicio sin
/// credenciales y cambiar de proveedor sin tocar la lógica de negocio.
/// </summary>
public interface IPushSender
{
    Task<ResultadoPush> EnviarAsync(string token, MensajePush mensaje, CancellationToken cancellationToken = default);
}
