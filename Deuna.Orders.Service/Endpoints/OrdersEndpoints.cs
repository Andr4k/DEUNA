using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Services;
using Deuna.Orders.Service.Validators;
using Deuna.Shared.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Deuna.Orders.Service.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
            .WithTags("Orders")
            .WithOpenApi()
            .RequireAuthorization("restaurant"); // Solo rol RESTAURANT

        group.MapPost("/", CrearPedidoAsync)
            .WithName("CrearPedido")
            .WithSummary("Crear un nuevo pedido")
            .Produces<CrearPedidoResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{pedidoId:guid}", ObtenerPedidoAsync)
            .WithName("ObtenerPedido")
            .WithSummary("Obtener un pedido por ID")
            .Produces<PedidoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/cliente/{clienteId:guid}", ObtenerPedidosPorClienteAsync)
            .WithName("ObtenerPedidosPorCliente")
            .WithSummary("Obtener pedidos de un cliente")
            .Produces<List<PedidoResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/restaurante/{restauranteId:guid}", ObtenerPedidosPorRestauranteAsync)
            .WithName("ObtenerPedidosPorRestaurante")
            .WithSummary("Obtener pedidos de un restaurante")
            .Produces<List<PedidoResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> CrearPedidoAsync(
        [FromBody] CrearPedidoRequest request,
        [FromServices] IPedidoService pedidoService,
        [FromServices] IValidator<CrearPedidoRequest> validator,
        HttpContext httpContext)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "Datos inválidos",
                Errors = validation.Errors.Select(e => e.ErrorMessage)
            });
        }

        try
        {
            var response = await pedidoService.CrearPedidoAsync(request, httpContext);
            return Results.Created($"/api/v1/orders/{response.PedidoId}", response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { Success = false, Message = ex.Message });
        }
        catch (Exception)
        {
            // Log the exception
            return Results.Problem(
                title: "Error interno del servidor",
                detail: "Ha ocurrido un error inesperado",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ObtenerPedidoAsync(
        Guid pedidoId,
        [FromServices] IPedidoService pedidoService,
        HttpContext httpContext)
    {
        var pedido = await pedidoService.ObtenerPedidoAsync(pedidoId);
        if (pedido == null)
        {
            return Results.NotFound(new { Success = false, Message = "Pedido no encontrado" });
        }

        // Verificar autorización: solo el cliente dueño o el restaurante pueden ver el pedido
        var userId = httpContext.GetUserId();
        var rol = httpContext.GetRole();

        if (userId != pedido.ClienteId && userId != pedido.RestauranteId && rol != "ADMIN")
        {
            return Results.Forbid();
        }

        return Results.Ok(pedido);
    }

    private static async Task<IResult> ObtenerPedidosPorClienteAsync(
        Guid clienteId,
        [FromServices] IPedidoService pedidoService,
        HttpContext httpContext)
    {
        var userId = httpContext.GetUserId();
        var rol = httpContext.GetRole();

        // Solo el propio cliente o admin pueden ver sus pedidos
        if (userId != clienteId && rol != "ADMIN")
        {
            return Results.Forbid();
        }

        var pedidos = await pedidoService.ObtenerPedidosPorClienteAsync(clienteId);
        return Results.Ok(pedidos);
    }

    private static async Task<IResult> ObtenerPedidosPorRestauranteAsync(
        Guid restauranteId,
        [FromServices] IPedidoService pedidoService,
        HttpContext httpContext)
    {
        var userId = httpContext.GetUserId();
        var rol = httpContext.GetRole();

        // Solo el propio restaurante o admin pueden ver sus pedidos
        if (userId != restauranteId && rol != "ADMIN")
        {
            return Results.Forbid();
        }

        var pedidos = await pedidoService.ObtenerPedidosPorRestauranteAsync(restauranteId);
        return Results.Ok(pedidos);
    }
}