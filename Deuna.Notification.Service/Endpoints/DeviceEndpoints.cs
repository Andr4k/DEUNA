using Deuna.Notification.Service.DTOs;
using Deuna.Notification.Service.Models;
using Deuna.Notification.Service.Services;
using Deuna.Shared.Extensions;
using FluentValidation;

namespace Deuna.Notification.Service.Endpoints;

/// <summary>
/// Registro de dispositivos para notificaciones push.
///
/// El usuario sale del token, nunca del cuerpo: si viniera del payload, cualquiera podría
/// registrar su propio teléfono como si fuera de otro y recibir sus notificaciones.
/// </summary>
public static class DeviceEndpoints
{
    private static readonly string[] PlataformasValidas =
        [Dispositivo.PlataformaAndroid, Dispositivo.PlataformaIos, "web"];

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications");

        grupo.MapPost("/devices", async (
            RegistrarDispositivoRequest request,
            INotificacionService notificaciones,
            IValidator<RegistrarDispositivoRequest> validator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var usuarioId = httpContext.GetUserId();
            if (usuarioId is null)
            {
                return Results.Unauthorized();
            }

            var dispositivo = await notificaciones.RegistrarDispositivoAsync(
                usuarioId.Value, request.Token, request.Plataforma, cancellationToken);

            return Results.Ok(new
            {
                dispositivoId = dispositivo.Id,
                plataforma = dispositivo.Plataforma,
                activo = dispositivo.Activo
            });
        })
        .RequireAuthorization()
        .WithName("RegisterDevice")
        .WithSummary("Registra el dispositivo del usuario autenticado para notificaciones push");

        grupo.MapPost("/devices/unregister", async (
            DesregistrarDispositivoRequest request,
            INotificacionService notificaciones,
            IValidator<DesregistrarDispositivoRequest> validator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var usuarioId = httpContext.GetUserId();
            if (usuarioId is null)
            {
                return Results.Unauthorized();
            }

            var desregistrado = await notificaciones.DesregistrarDispositivoAsync(
                usuarioId.Value, request.Token, cancellationToken);

            return desregistrado
                ? Results.Ok(new { message = "Dispositivo desregistrado" })
                : Results.NotFound(new { message = "El dispositivo no estaba registrado para este usuario" });
        })
        .RequireAuthorization()
        .WithName("UnregisterDevice")
        .WithSummary("Desregistra el dispositivo del usuario autenticado");

        return app;
    }

    public static bool EsPlataformaValida(string plataforma) =>
        PlataformasValidas.Contains(plataforma, StringComparer.OrdinalIgnoreCase);
}
