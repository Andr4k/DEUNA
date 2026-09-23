using Deuna.Delivery.Service.DTOs;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Extensions;
using FluentValidation;

namespace Deuna.Delivery.Service.Endpoints;

/// <summary>
/// Endpoints de tracking GPS (US-003.3 del plan técnico: /api/v1/delivery).
/// </summary>
public static class TrackingEndpoints
{
    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/delivery")
            .WithTags("Delivery Tracking");

        // POST /api/v1/delivery/update-location — telemetría GPS del repartidor
        grupo.MapPost("/update-location", async (
            ActualizarUbicacionRequest request,
            ITrackingService trackingService,
            IValidator<ActualizarUbicacionRequest> validator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var autenticado = httpContext.GetUserId();
            if (autenticado is null)
            {
                return Results.Unauthorized();
            }

            // Un repartidor solo puede reportar su propia ubicación: sin esta verificación
            // cualquiera con rol RIDER podría falsear la posición de otro.
            if (autenticado.Value != request.RiderId)
            {
                return Results.Problem(
                    title: "Ubicación no autorizada",
                    detail: "Un repartidor solo puede reportar su propia ubicación",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await trackingService.ActualizarUbicacionAsync(
                request.RiderId, request.Latitude, request.Longitude, request.Timestamp, cancellationToken);

            return Results.NoContent();
        })
        .RequireAuthorization("rider")
        .WithName("UpdateRiderLocation")
        .WithSummary("Registra la posición GPS del repartidor autenticado");

        // GET /api/v1/delivery/tracking/{orderId} — ubicación en tiempo real del repartidor
        grupo.MapGet("/tracking/{orderId:guid}", async (
            Guid orderId,
            ITrackingService trackingService,
            CancellationToken cancellationToken) =>
        {
            var ubicacion = await trackingService.ObtenerUbicacionDePedidoAsync(orderId, cancellationToken);

            if (ubicacion is null)
            {
                return Results.NotFound(new
                {
                    message = $"No hay ubicación disponible para el pedido {orderId}"
                });
            }

            return Results.Ok(new UbicacionRepartidorResponse(
                OrderId: orderId,
                RiderId: ubicacion.RiderId,
                Latitude: ubicacion.Latitud,
                Longitude: ubicacion.Longitud,
                DistanciaMetros: ubicacion.DistanciaMetros,
                ActualizadoEn: ubicacion.ActualizadoEn));
        })
        .RequireAuthorization()
        .WithName("GetOrderTracking")
        .WithSummary("Devuelve la ubicación del repartidor asociado al pedido");

        return app;
    }
}
