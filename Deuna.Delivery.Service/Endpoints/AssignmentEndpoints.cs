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

        // POST /api/v1/delivery/validate-qr-local — el domiciliario escanea el QR del local
        grupo.MapPost("/validate-qr-local", async (
            ValidarQrLocalRequest request,
            IAsignacionService asignacion,
            IValidator<ValidarQrLocalRequest> validator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            // El domiciliario sale del token: el cuerpo no puede decir quién es.
            var repartidorId = httpContext.GetUserId();
            if (repartidorId is null)
            {
                return Results.Unauthorized();
            }

            var resultado = await asignacion.ValidarQrLocalAsync(
                request.OrderId, repartidorId.Value, request.TokenQrLocal, cancellationToken);

            if (!resultado.Valido)
            {
                return resultado.Motivo switch
                {
                    MotivoRechazo.PedidoNoEncontrado => Results.NotFound(new { message = resultado.Mensaje }),
                    MotivoRechazo.NoAsignado => Results.StatusCode(StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { message = resultado.Mensaje })
                };
            }

            return Results.Ok(new
            {
                message = resultado.Mensaje,
                estado = resultado.Estado,
                fechaLlegadaLocal = resultado.FechaLlegadaLocal,
                // El paso siguiente del flujo (TASK-305) queda habilitado.
                puedeIniciarEntrega = true
            });
        })
        .RequireAuthorization("rider")
        .WithName("ValidateLocalQr")
        .WithSummary("Valida el QR que muestra el restaurante y confirma la llegada al local");

        // POST /api/v1/delivery/start-delivery/{orderId} — el domiciliario inicia la entrega
        grupo.MapPost("/start-delivery/{orderId:guid}", async (
            Guid orderId,
            IAsignacionService asignacion,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var repartidorId = httpContext.GetUserId();
            if (repartidorId is null)
            {
                return Results.Unauthorized();
            }

            var resultado = await asignacion.IniciarEntregaAsync(orderId, repartidorId.Value, cancellationToken);

            if (!resultado.Iniciada)
            {
                return resultado.Motivo switch
                {
                    MotivoRechazo.PedidoNoEncontrado => Results.NotFound(new { message = resultado.Mensaje }),
                    MotivoRechazo.NoAsignado => Results.StatusCode(StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { message = resultado.Mensaje })
                };
            }

            return Results.Ok(new
            {
                message = resultado.Mensaje,
                estado = resultado.Estado,
                // Mientras está en ruta, la app del cliente puede seguir la telemetría.
                puedeSeguirTracking = true
            });
        })
        .RequireAuthorization("rider")
        .WithName("StartDelivery")
        .WithSummary("Inicia la entrega del pedido ya confirmado en el local (estado EnRuta)");

        // POST /api/v1/delivery/validate-qr-delivery — el CLIENTE escanea el QR de cierre.
        //
        // Es público a propósito: quien escanea es el cliente, que no tiene sesión. El
        // token del QR es lo único que autoriza el cierre, y es de un solo uso.
        grupo.MapPost("/validate-qr-delivery", async (
            ValidarQrEntregaRequest request,
            IAsignacionService asignacion,
            IValidator<ValidarQrEntregaRequest> validator,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var resultado = await asignacion.CerrarEntregaAsync(
                request.OrderId, request.TokenQrEntrega, cancellationToken);

            if (!resultado.Cerrado)
            {
                return resultado.Motivo switch
                {
                    MotivoRechazo.PedidoNoEncontrado => Results.NotFound(new { message = resultado.Mensaje }),
                    _ => Results.BadRequest(new { message = resultado.Mensaje })
                };
            }

            return Results.Ok(new
            {
                message = resultado.Mensaje,
                estado = resultado.Estado,
                fechaEntrega = resultado.FechaEntrega,
                // El paso siguiente del flujo es la encuesta (TASK-401).
                puedeCalificar = true
            });
        })
        .AllowAnonymous()
        .WithName("ValidateDeliveryQr")
        .WithSummary("Cierra la entrega validando el QR que muestra el domiciliario (público)");

        return app;
    }
}
