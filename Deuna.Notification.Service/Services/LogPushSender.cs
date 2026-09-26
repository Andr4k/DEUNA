namespace Deuna.Notification.Service.Services;

/// <summary>
/// Emisor de desarrollo: registra la notificación en el log en lugar de enviarla.
///
/// Es el que se usa cuando no hay credenciales de FCM configuradas. Permite correr el
/// pipeline completo (evento → resolución del destinatario → registro) sin depender de
/// Firebase, y ver en el log exactamente qué se habría enviado.
/// </summary>
public class LogPushSender : IPushSender
{
    public const string NombreProveedor = "log";

    private readonly ILogger<LogPushSender> _logger;

    public LogPushSender(ILogger<LogPushSender> logger)
    {
        _logger = logger;
    }

    public Task<ResultadoPush> EnviarAsync(string token, MensajePush mensaje, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[PUSH SIMULADO] token={Token} titulo=\"{Titulo}\" cuerpo=\"{Cuerpo}\" datos={Datos}",
            RecortarToken(token), mensaje.Titulo, mensaje.Cuerpo,
            string.Join(", ", mensaje.Datos.Select(d => $"{d.Key}={d.Value}")));

        return Task.FromResult(new ResultadoPush(true, NombreProveedor));
    }

    /// <summary>Un token es una credencial: no se escribe entero en el log.</summary>
    private static string RecortarToken(string token) =>
        token.Length <= 8 ? "***" : $"{token[..8]}…";
}
