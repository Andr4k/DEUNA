using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace Deuna.Orders.Service.Endpoints;

/// <summary>
/// Rutas del panel de administración. Van en un grupo propio con la política "admin":
/// el grupo existente de <c>/api/v1/orders</c> exige "restaurant", así que un
/// administrador no entraría por ahí.
/// </summary>
public static class AdminOrdersEndpoints
{
    public static IEndpointRouteBuilder MapAdminOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/orders")
            .WithTags("Admin Orders")
            .WithOpenApi()
            .RequireAuthorization("admin"); // Solo rol ADMIN

        group.MapGet("/sin-asignar", ObtenerPedidosSinAsignarAsync)
            .WithName("ObtenerPedidosSinAsignar")
            .WithSummary("Listar los pedidos sin asignar para el panel del administrador")
            .Produces<ListaPedidosSinAsignarResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ObtenerPedidosSinAsignarAsync(
        [FromServices] IPedidoService pedidoService,
        string? zona = null,
        string? prioridad = null,
        int? esperaMin = null,
        bool incluirSinRespuesta = false,
        int pagina = 1,
        int tamano = 10)
    {
        // El motivo va tipado para que el portal sepa por qué se rechazó sin leer el texto.
        if (!string.IsNullOrWhiteSpace(prioridad) &&
            prioridad.Trim() is not (OrdersOptions.PrioridadAlta or OrdersOptions.PrioridadMedia or OrdersOptions.PrioridadBaja))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "prioridad debe ser Alta, Media o Baja"
            });
        }

        if (pagina < 1 || tamano < 1)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "pagina y tamano deben ser mayores a cero"
            });
        }

        var filtro = new FiltroPedidosSinAsignar(
            Zona: zona,
            Prioridad: prioridad,
            EsperaMin: esperaMin,
            IncluirSinRespuesta: incluirSinRespuesta,
            Pagina: pagina,
            Tamano: tamano);

        var lista = await pedidoService.ObtenerPedidosSinAsignarAsync(filtro);
        return Results.Ok(lista);
    }
}
