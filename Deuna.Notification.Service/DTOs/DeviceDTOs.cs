using System.ComponentModel.DataAnnotations;

namespace Deuna.Notification.Service.DTOs;

/// <summary>
/// Registro del dispositivo del usuario autenticado. El token lo emite FCM en la app y
/// se envía acá para que el backend sepa a dónde notificar.
/// </summary>
public record RegistrarDispositivoRequest(
    [Required, MaxLength(500)] string Token,
    [Required, MaxLength(20)] string Plataforma);

/// <summary>
/// Desregistro del dispositivo (cierre de sesión). El token va en el cuerpo y no en la
/// URL a propósito: es una credencial y las URLs quedan en logs y proxies.
/// </summary>
public record DesregistrarDispositivoRequest(
    [Required, MaxLength(500)] string Token);
