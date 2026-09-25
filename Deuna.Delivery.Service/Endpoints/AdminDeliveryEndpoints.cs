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

            // El código HTTP sale del MOTIVO, no del texto del mensaje: el día que alguien
            // corrija una coma, el comportamiento no puede cambiar.
            return RespuestaDelMotivo(resultado.Mensaje, resultado.Motivo);
        })
        .WithName("AsignarPedidoManualmente")
        .WithSummary("Asigna a mano un pedido al repartidor elegido: tiene que estar en el radio y libre")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // GET /api/v1/admin/delivery/candidatos/{pedidoId} — a quién le puede caer el pedido
        grupo.MapGet("/candidatos/{pedidoId:guid}", async (
            Guid pedidoId,
            IAsignacionService asignacion,
            CancellationToken cancellationToken) =>
        {
            var resultado = await asignacion.ObtenerCandidatosAsync(pedidoId, cancellationToken);

            if (!resultado.Exito)
            {
                return RespuestaDelMotivo(resultado.Mensaje, resultado.Motivo);
            }

            return Results.Ok(new
            {
                pedidoId = resultado.PedidoId,
                codigo = resultado.Codigo,
                radioKm = resultado.RadioKm,
                total = resultado.Candidatos!.Count,
                candidatos = resultado.Candidatos
            });
        })
        .WithName("ObtenerCandidatosDelPedido")
        .WithSummary("Lista los repartidores dentro del radio del pedido, con su disponibilidad")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/admin/delivery/mapa — las tres capas del mapa de la operación
        grupo.MapGet("/mapa", async (
            IMapaService mapa,
            CancellationToken cancellationToken) =>
        {
            var operacion = await mapa.ObtenerAsync(cancellationToken);

            return Results.Ok(operacion);
        })
        .WithName("ObtenerMapaOperacion")
        .WithSummary("Devuelve el mapa de la operación: domiciliarios en vivo, pedidos activos y restaurantes")
        .Produces<MapaOperacion>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Traduce el motivo a un código HTTP. Vive en un solo lugar porque las dos rutas del
    /// grupo tienen que responder lo mismo para el mismo motivo: si cada una lo decidiera
    /// por su cuenta, un día el portal recibiría 400 de una y 409 de la otra.
    /// </summary>
    private static IResult RespuestaDelMotivo(string mensaje, MotivoAsignacion motivo)
    {
        var cuerpo = new { message = mensaje, motivo = motivo.ToString() };

        return motivo switch
        {
            MotivoAsignacion.PedidoNoEncontrado => Results.NotFound(cuerpo),
            MotivoAsignacion.EstadoInvalido or MotivoAsignacion.SinPuntoEntrega =>
                Results.BadRequest(cuerpo),
            _ => Results.Conflict(cuerpo)
        };
    }
}
