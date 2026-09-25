using Deuna.Delivery.Service.DTOs;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Extensions;
using FluentValidation;

namespace Deuna.Delivery.Service.Endpoints;

/// <summary>
/// Endpoints del panel de administración sobre la entrega.
///
/// Van en su propio grupo con la política "admin": todos los endpoints de asignación que
/// existían son del repartidor ("rider") o anónimos, así que un administrador no entra por
/// ninguno. Y como el gateway enruta por prefijo, `/api/v1/admin/delivery/**` necesita
/// además su ruta en el gateway.
/// </summary>
public static class AdminDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapAdminDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/admin/delivery")
            .WithTags("Admin Delivery")
            .RequireAuthorization("admin");

        // POST /api/v1/admin/delivery/asignar/{pedidoId} — el operador elige al repartidor
        grupo.MapPost("/asignar/{pedidoId:guid}", async (
            Guid pedidoId,
            AsignarPedidoRequest request,
            IAsignacionService asignacion,
            IValidator<AsignarPedidoRequest> validator,
            CancellationToken cancellationToken) =>
        {
            var validacion = await validator.ValidateAsync(request, cancellationToken);
            if (!validacion.IsValid)
            {
                return Results.ValidationProblem(validacion.ToDictionary());
            }

            var resultado = await asignacion.AsignarManualmenteAsync(
                pedidoId, request.RepartidorId, cancellationToken);

            if (resultado.Asignado)
            {
                return Results.Ok(new
                {
                    message = resultado.Mensaje,
                    pedidoId,
                    repartidorId = resultado.RepartidorId,
                    distanciaMetros = resultado.DistanciaMetros
                });
            }

            // El código HTTP sale del MOTIVO, no del texto del mensaje. Un pedido que ya no
            // está en búsqueda y un repartidor que no es candidato le piden al operador cosas
            // distintas, y el portal tiene que poder distinguirlas sin interpretar un texto:
            // el día que alguien corrija una coma, el comportamiento no puede cambiar.
            var cuerpo = new { message = resultado.Mensaje, motivo = resultado.Motivo.ToString() };

            return resultado.Motivo switch
            {
                MotivoAsignacion.PedidoNoEncontrado => Results.NotFound(cuerpo),
                MotivoAsignacion.EstadoInvalido or MotivoAsignacion.SinPuntoEntrega =>
                    Results.BadRequest(cuerpo),
                _ => Results.Conflict(cuerpo)
            };
        })
        .WithName("AsignarPedidoManualmente")
        .WithSummary("Asigna a mano un pedido al repartidor elegido: tiene que estar en el radio y libre")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
