using Deuna.Notification.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Notification.Service.Services;

public record ResultadoNotificacion(
    bool Enviada,
    string Estado,
    int Dispositivos,
    string? Error = null);

/// <summary>
/// Resuelve a quién notificar, delega el envío en <see cref="IPushSender"/> y deja el
/// registro de lo que pasó.
///
/// No conoce reglas de negocio de pedidos: recibe un destinatario, un tipo de evento y un
/// mensaje ya redactado. Esa separación es lo que permite que el mismo servicio notifique
/// a domiciliarios y restaurantes sin duplicar la integración con el proveedor.
/// </summary>
public interface INotificacionService
{
    /// <summary>Registra o actualiza el dispositivo del usuario autenticado.</summary>
    Task<Dispositivo> RegistrarDispositivoAsync(Guid usuarioId, string token, string plataforma, CancellationToken cancellationToken = default);

    /// <summary>Desactiva un dispositivo (por ejemplo, al cerrar sesión).</summary>
    Task<bool> DesregistrarDispositivoAsync(Guid usuarioId, string token, CancellationToken cancellationToken = default);

    /// <summary>Notifica a todos los dispositivos activos del destinatario.</summary>
    Task<ResultadoNotificacion> NotificarAsync(
        Guid destinatarioId,
        string tipoEvento,
        MensajePush mensaje,
        Guid? mensajeId = null,
        CancellationToken cancellationToken = default);
}

public class NotificacionService : INotificacionService
{
    private const int MaximoLargoError = 1000;

    private readonly NotificationDbContext _db;
    private readonly IPushSender _push;
    private readonly ILogger<NotificacionService> _logger;

    public NotificacionService(NotificationDbContext db, IPushSender push, ILogger<NotificacionService> logger)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    public async Task<Dispositivo> RegistrarDispositivoAsync(
        Guid usuarioId,
        string token,
        string plataforma,
        CancellationToken cancellationToken = default)
    {
        var existente = await _db.Dispositivos
            .FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

        if (existente is not null)
        {
            // El token es del dispositivo: si otro usuario inició sesión en el mismo
            // teléfono, se reasigna en lugar de crear una segunda fila.
            existente.UsuarioId = usuarioId;
            existente.Plataforma = plataforma;
            existente.Activo = true;
            existente.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return existente;
        }

        var dispositivo = new Dispositivo
        {
            UsuarioId = usuarioId,
            Token = token,
            Plataforma = plataforma,
            Activo = true
        };

        _db.Dispositivos.Add(dispositivo);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Dispositivo registrado para {UsuarioId} ({Plataforma})", usuarioId, plataforma);
        return dispositivo;
    }

    public async Task<bool> DesregistrarDispositivoAsync(
        Guid usuarioId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var dispositivo = await _db.Dispositivos
            .FirstOrDefaultAsync(d => d.Token == token && d.UsuarioId == usuarioId, cancellationToken);

        if (dispositivo is null)
        {
            return false;
        }

        dispositivo.Activo = false;
        dispositivo.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<ResultadoNotificacion> NotificarAsync(
        Guid destinatarioId,
        string tipoEvento,
        MensajePush mensaje,
        Guid? mensajeId = null,
        CancellationToken cancellationToken = default)
    {
        // Idempotencia: el broker puede reentregar el evento, y notificar dos veces al
        // mismo usuario por el mismo hecho es un defecto visible en la app.
        if (mensajeId is not null)
        {
            var yaProcesado = await _db.NotificacionesEnviadas
                .AnyAsync(n => n.MensajeId == mensajeId, cancellationToken);

            if (yaProcesado)
            {
                _logger.LogDebug("El mensaje {MensajeId} ya fue notificado: no se reenvía", mensajeId);
                return new ResultadoNotificacion(false, "YaProcesada", 0);
            }
        }

        var dispositivos = await _db.Dispositivos
            .Where(d => d.UsuarioId == destinatarioId && d.Activo)
            .ToListAsync(cancellationToken);

        var registro = new NotificacionEnviada
        {
            MensajeId = mensajeId,
            TipoEvento = tipoEvento,
            DestinatarioId = destinatarioId,
            Titulo = mensaje.Titulo,
            Cuerpo = mensaje.Cuerpo,
            Dispositivos = dispositivos.Count
        };

        if (dispositivos.Count == 0)
        {
            // Queda el rastro: "no se enteró" y "no tenía la app" son cosas distintas.
            registro.Estado = NotificacionEnviada.EstadoSinDispositivo;
            _db.NotificacionesEnviadas.Add(registro);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Sin dispositivos activos para {DestinatarioId}: no se pudo notificar {TipoEvento}",
                destinatarioId, tipoEvento);

            return new ResultadoNotificacion(false, registro.Estado, 0);
        }

        var exitos = 0;
        var errores = new List<string>();
        string? proveedor = null;

        foreach (var dispositivo in dispositivos)
        {
            var resultado = await _push.EnviarAsync(dispositivo.Token, mensaje, cancellationToken);
            proveedor ??= resultado.Proveedor;

            if (resultado.Exito)
            {
                exitos++;
                dispositivo.UltimoUsoAt = DateTime.UtcNow;
                continue;
            }

            if (resultado.TokenInvalido)
            {
                // El proveedor dice que el token murió: dejar de intentar contra él.
                dispositivo.Activo = false;
                dispositivo.UpdatedAt = DateTime.UtcNow;
            }

            errores.Add(resultado.Error ?? "error desconocido del proveedor");
        }

        registro.Estado = exitos > 0 ? NotificacionEnviada.EstadoEnviada : NotificacionEnviada.EstadoFallida;
        registro.Proveedor = proveedor;
        registro.Error = errores.Count > 0 ? Recortar(string.Join(" | ", errores)) : null;
        registro.EnviadoAt = exitos > 0 ? DateTime.UtcNow : null;

        _db.NotificacionesEnviadas.Add(registro);
        await _db.SaveChangesAsync(cancellationToken);

        if (exitos > 0)
        {
            _logger.LogInformation(
                "{TipoEvento}: notificado a {Exitos}/{Total} dispositivo(s) de {DestinatarioId}",
                tipoEvento, exitos, dispositivos.Count, destinatarioId);
        }
        else
        {
            _logger.LogWarning(
                "{TipoEvento}: no se pudo notificar a ningún dispositivo de {DestinatarioId} ({Error})",
                tipoEvento, destinatarioId, registro.Error);
        }

        return new ResultadoNotificacion(exitos > 0, registro.Estado, dispositivos.Count, registro.Error);
    }

    private static string Recortar(string texto) =>
        texto.Length <= MaximoLargoError ? texto : texto[..MaximoLargoError];
}
