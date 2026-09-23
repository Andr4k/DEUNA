using Deuna.Delivery.Service.DTOs;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Extensions;
using FluentValidation;

namespace Deuna.Delivery.Service.Endpoints;

/// <summary>
/// Endpoints de la asignación automática (TASK-303).
///
/// No hay lista de pedidos disponibles ni aceptación manual: el sistema asigna. Al
/// repartidor solo le quedan dos acciones: ver lo que tiene asignado y rechazarlo
/// antes de llegar al local.
/// </summary>
public static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/delivery")
            .WithTags("Delivery Assignment");

        // GET /api/v1/delivery/my-orders — pedidos asignados al repartidor autenticado
        grupo.MapGet("/my-orders", async (
            IAsignacionService asignacion,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var repartidorId = httpContext.GetUserId();
            if (repartidorId is null)
            {
                return Results.Unauthorized();
            }

            // Solo los propios: el id sale del token, nunca de la petición.
            var pedidos = await asignacion.ObtenerAsignadosAsync(repartidorId.Value, cancellationToken);
            return Results.Ok(pedidos);
        })
        .RequireAuthorization("rider")
        .WithName("GetMyOrders")
        .WithSummary("Pedidos asignados automáticamente al repartidor autenticado");

        // POST /api/v1/delivery/reject-order/{orderId} — rechazo antes de llegar al local
        grupo.MapPost("/reject-order/{orderId:guid}", async (
            Guid orderId,
            RechazarPedidoRequest? request,
            IAsignacionService asignacion,
            IValidator<RechazarPedidoRequest> validator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request ?? new RechazarPedidoRequest(null), cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var repartidorId = httpContext.GetUserId();
            if (repartidorId is null)
            {
                return Results.Unauthorized();
            }

            var resultado = await asignacion.RechazarAsync(
                orderId, repartidorId.Value, request?.Motivo, cancellationToken);

            // El servicio verifica que el pedido sea de este repartidor: un 400 aquí también
            // cubre el intento de rechazar un pedido ajeno.
            return resultado.Asignado || resultado.RepartidorId is not null
                ? Results.Ok(new { message = resultado.Mensaje, reasignado = resultado.Asignado, repartidorId = resultado.RepartidorId })
                : Results.BadRequest(new { message = resultado.Mensaje });
        })
        .RequireAuthorization("rider")
        .WithName("RejectOrder")
        .WithSummary("Rechaza el pedido asignado antes de llegar al local (dispara reasignación)");

        return app;
    }
}
