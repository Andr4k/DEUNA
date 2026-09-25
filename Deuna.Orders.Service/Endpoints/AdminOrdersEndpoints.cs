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

        group.MapGet("/servicios-finalizados", ObtenerServiciosFinalizadosAsync)
            .WithName("ObtenerServiciosFinalizados")
            .WithSummary("Historial de servicios finalizados con filtros, paginación y KPIs")
            .Produces<RespuestaServiciosFinalizados>(StatusCodes.Status200OK)
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

    /// <summary>Los dos valores canónicos del contrato para "quién paga" el domicilio.</summary>
    private const string PagoCliente = "Cliente";
    private const string PagoDomiciliario = "Domiciliario";

    private static async Task<IResult> ObtenerServiciosFinalizadosAsync(
        [FromServices] IPedidoService pedidoService,
        DateTime? desde = null,
        DateTime? hasta = null,
        Guid? restauranteId = null,
        Guid? repartidorId = null,
        string? zona = null,
        double? calificacionMin = null,
        bool conIncidencia = false,
        string? pago = null,
        string? buscar = null,
        int pagina = 1,
        int tamano = 20)
    {
        if (pagina < 1 || tamano < 1)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "pagina y tamano deben ser mayores a cero"
            });
        }

        if (desde is not null && hasta is not null && desde >= hasta)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "El rango de fechas está invertido: desde tiene que ser anterior a hasta"
            });
        }

        // Las notas son 1..5. Un 9 no puede devolver una lista vacía en silencio: en pantalla
        // se leería como "no hay servicios con esa nota", y lo que pasa es que la nota pedida
        // no existe.
        if (calificacionMin is { } minima && (minima < 1 || minima > 5))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "calificacionMin debe estar entre 1 y 5"
            });
        }

        // Dos filtros del mockup todavía no tienen fuente: no existe el modelo de incidencias
        // y "quién paga" sigue pendiente de decisión en el plan. Un conjunto vacío se leería
        // como "no hubo incidencias" o "nadie pagó el domicilio", que es lo contrario de "no
        // se puede saber": por eso la petición se rechaza en vez de mentir con un resultado.
        if (conIncidencia)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "El filtro conIncidencia todavía no tiene fuente: no existe el modelo de incidencias"
            });
        }

        if (pago is not null)
        {
            if (pago.Trim() is not (PagoCliente or PagoDomiciliario))
            {
                return Results.BadRequest(new
                {
                    Success = false,
                    Message = "pago debe ser Cliente o Domiciliario"
                });
            }

            return Results.BadRequest(new
            {
                Success = false,
                Message = "El filtro pago todavía no tiene fuente: no está decidido de dónde sale quién paga el domicilio"
            });
        }

        var filtro = new FiltroServiciosFinalizados(
            Desde: desde,
            Hasta: hasta,
            RestauranteId: restauranteId,
            RepartidorId: repartidorId,
            Zona: zona,
            CalificacionMin: calificacionMin,
            Buscar: buscar,
            Pagina: pagina,
            Tamano: tamano);

        var respuesta = await pedidoService.ObtenerServiciosFinalizadosAsync(filtro);
        return Results.Ok(respuesta);
    }
}
