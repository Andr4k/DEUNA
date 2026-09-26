using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace Deuna.Feedback.Service.Endpoints;

/// <summary>
/// Rutas del panel de administración para las calificaciones a restaurantes (sección 4 del plan).
///
/// Van en un grupo propio con la política "admin": el grupo de <c>/api/v1/feedback</c> lo usan
/// los clientes y los repartidores para *enviar* encuestas y sus rutas son anónimas, así que
/// mezclar acá la lectura del panel dejaría el desempeño de los restaurantes al alcance de
/// cualquiera con el enlace. Es la misma separación que ya tiene Orders.
/// </summary>
public static class AdminFeedbackEndpoints
{
    /// <summary>
    /// El único orden que la lista implementa (sección 4.1 del plan): promedio más alto primero.
    /// Se declara para poder rechazar cualquier otro valor en vez de ignorarlo.
    /// </summary>
    private const string OrdenPromedio = "promedio";

    public static IEndpointRouteBuilder MapAdminFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/admin/feedback")
            .WithTags("Admin Feedback")
            .WithOpenApi()
            .RequireAuthorization("admin"); // Solo rol ADMIN

        grupo.MapGet("/restaurantes", ObtenerCalificacionesAsync)
            .WithName("ObtenerCalificacionesRestaurantes")
            .WithSummary("Lista paginada de restaurantes con sus calificaciones agregadas del rango")
            .Produces<PaginaCalificacionesRestaurantes>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        grupo.MapGet("/restaurantes/resumen", ObtenerResumenAsync)
            .WithName("ObtenerResumenCalificacionesRestaurantes")
            .WithSummary("KPIs de la cabecera, calculados sobre el mismo conjunto filtrado que la lista")
            .Produces<ResumenCalificacionesRestaurantes>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        grupo.MapGet("/restaurantes/{restauranteId:guid}", ObtenerDetalleAsync)
            .WithName("ObtenerCalificacionDeRestaurante")
            .WithSummary("La fila de un restaurante, del mismo conjunto filtrado que la tabla")
            .Produces<CalificacionDeRestaurante>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// La lista de la tabla. Acepta los filtros que definen el conjunto, y son exactamente los
    /// mismos que acepta el resumen: si cada endpoint tuviera su propio WHERE, la cabecera y la
    /// tabla terminarían contando cosas distintas.
    /// </summary>
    private static async Task<IResult> ObtenerCalificacionesAsync(
        [FromServices] ICalificacionesRestaurantesService calificaciones,
        DateTimeOffset? desde = null,
        DateTimeOffset? hasta = null,
        string? buscar = null,
        string? zona = null,
        string? tipoDeComida = null,
        double? calificacionMin = null,
        double? calificacionMax = null,
        string? orden = null,
        int pagina = 1,
        int tamano = 20)
    {
        if (ValidarVentana(desde, hasta) is { } problemaDeVentana)
        {
            return problemaDeVentana;
        }

        if (pagina < 1 || tamano < 1)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "pagina y tamano deben ser mayores a cero"
            });
        }

        if (ValidarFiltrosCompartidos(calificacionMin, calificacionMax, tipoDeComida) is { } problemaDeFiltros)
        {
            return problemaDeFiltros;
        }

        // Un orden que no se puede honrar devolvería la misma lista como si hubiera reordenado,
        // y el portal leería ese resultado como el orden que pidió.
        if (!string.IsNullOrWhiteSpace(orden)
            && !string.Equals(orden.Trim(), OrdenPromedio, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = $"orden solo acepta {OrdenPromedio}: es el único orden de la lista (promedio descendente)"
            });
        }

        var filtro = ArmarFiltro(desde, hasta, buscar, zona, calificacionMin, calificacionMax, pagina, tamano);

        return Results.Ok(await calificaciones.ObtenerCalificacionesAsync(filtro));
    }

    /// <summary>
    /// Los KPIs de la cabecera. Recibe los mismos filtros que la lista —y no solo la ventana—
    /// porque el resumen se calcula sobre el resultado ya filtrado (sección 4.2 del plan): el
    /// portal manda la misma query a los dos endpoints, y así el número de arriba y la tabla de
    /// abajo no pueden discrepar.
    /// </summary>
    private static async Task<IResult> ObtenerResumenAsync(
        [FromServices] ICalificacionesRestaurantesService calificaciones,
        DateTimeOffset? desde = null,
        DateTimeOffset? hasta = null,
        string? buscar = null,
        string? zona = null,
        string? tipoDeComida = null,
        double? calificacionMin = null,
        double? calificacionMax = null)
    {
        if (ValidarVentana(desde, hasta) is { } problemaDeVentana)
        {
            return problemaDeVentana;
        }

        if (ValidarFiltrosCompartidos(calificacionMin, calificacionMax, tipoDeComida) is { } problemaDeFiltros)
        {
            return problemaDeFiltros;
        }

        // Pagina y tamano no se declaran: el resumen es del conjunto, no de la página, así que
        // no hay nada que paginar acá.
        var filtro = ArmarFiltro(desde, hasta, buscar, zona, calificacionMin, calificacionMax, 1, 20);

        return Results.Ok(await calificaciones.ObtenerResumenAsync(filtro));
    }

    /// <summary>
    /// El "ver detalle" de la tabla: la misma fila que muestra la lista, leída del mismo
    /// conjunto filtrado. La ventana es obligatoria por la misma razón que en la lista —el
    /// agregado es del rango—; el resto de los filtros no, porque el detalle se abre desde una
    /// fila que ya los cumple.
    /// </summary>
    private static async Task<IResult> ObtenerDetalleAsync(
        [FromServices] ICalificacionesRestaurantesService calificaciones,
        Guid restauranteId,
        DateTimeOffset? desde = null,
        DateTimeOffset? hasta = null)
    {
        if (ValidarVentana(desde, hasta) is { } problemaDeVentana)
        {
            return problemaDeVentana;
        }

        var filtro = ArmarFiltro(desde, hasta, buscar: null, zona: null, calificacionMin: null, calificacionMax: null, pagina: 1, tamano: 20);

        var fila = await calificaciones.ObtenerCalificacionAsync(restauranteId, filtro);

        // 404 y no una fila en blanco: sin calificaciones en la ventana no hay promedio, y una
        // fila con el promedio en null se leería como "este restaurante sacó cero".
        return fila is null
            ? Results.NotFound(new
            {
                Success = false,
                Message = $"El restaurante {restauranteId} no tiene calificaciones en el rango pedido"
            })
            : Results.Ok(fila);
    }

    /// <summary>
    /// La ventana es obligatoria: <c>desde</c> sin él la consulta devolvería todo el histórico y
    /// el filtro de fechas se leería como aplicado. <c>hasta</c> es exclusivo y opcional, con el
    /// mismo criterio que la sección 4.1 (<c>CreatedAt &gt;= desde and CreatedAt &lt; hasta</c>).
    ///
    /// Las fechas se reciben como <see cref="DateTimeOffset"/> y no como <c>DateTime</c>: una
    /// fecha sin zona llega con <c>Kind = Unspecified</c> y Npgsql la rechaza contra una columna
    /// <c>timestamp with time zone</c>, así que la conversión a UTC se hace acá, en el borde.
    /// </summary>
    private static IResult? ValidarVentana(DateTimeOffset? desde, DateTimeOffset? hasta)
    {
        if (desde is null)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "desde es obligatorio: sin ventana la lista y el resumen no salen del mismo conjunto"
            });
        }

        if (hasta is { } extremo && desde.Value >= extremo)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "El rango de fechas está invertido: desde tiene que ser anterior a hasta"
            });
        }

        return null;
    }

    /// <summary>
    /// Los filtros que comparten la lista y el resumen. El rango de calificación se valida por
    /// el mismo motivo que en el panel de Orders: un 9 no puede devolver un conjunto vacío en
    /// silencio, porque en pantalla se lee como "no hay restaurantes con esa nota".
    /// </summary>
    private static IResult? ValidarFiltrosCompartidos(
        double? calificacionMin,
        double? calificacionMax,
        string? tipoDeComida)
    {
        if (calificacionMin is { } minima && (minima < 1 || minima > 5))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "calificacionMin debe estar entre 1 y 5"
            });
        }

        if (calificacionMax is { } maxima && (maxima < 1 || maxima > 5))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "calificacionMax debe estar entre 1 y 5"
            });
        }

        if (calificacionMin is { } piso && calificacionMax is { } techo && piso > techo)
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "El rango de calificación está invertido: calificacionMin no puede ser mayor que calificacionMax"
            });
        }

        // El tipo de comida no existe en ningún servicio (sección 2 del plan): se rechaza en vez
        // de ignorarlo, porque un filtro que se acepta y no se aplica devuelve el conjunto entero
        // y se lee como si hubiera filtrado.
        if (!string.IsNullOrWhiteSpace(tipoDeComida))
        {
            return Results.BadRequest(new
            {
                Success = false,
                Message = "El filtro tipoDeComida todavía no tiene fuente: ningún servicio guarda el tipo de comida del restaurante"
            });
        }

        return null;
    }

    /// <summary>
    /// El filtro compartido: lo arman los tres endpoints en un solo lugar, y por eso la lista y
    /// el resumen no pueden terminar con dos WHERE parecidos que diverjan.
    /// </summary>
    private static FiltroCalificacionesRestaurantes ArmarFiltro(
        DateTimeOffset? desde,
        DateTimeOffset? hasta,
        string? buscar,
        string? zona,
        double? calificacionMin,
        double? calificacionMax,
        int pagina,
        int tamano)
        => new(
            Desde: desde!.Value.UtcDateTime,
            Hasta: hasta?.UtcDateTime,
            Buscar: buscar,
            Zona: zona,
            CalificacionMin: calificacionMin,
            CalificacionMax: calificacionMax,
            Pagina: pagina,
            Tamano: tamano);
}
