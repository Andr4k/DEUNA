using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Services;
using Deuna.Identity.Service.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Deuna.Identity.Service.Endpoints;

/// <summary>
/// Endpoints del área admin de Identity: lo que consume la pantalla de gestión de
/// usuarios del portal. Van bajo la política `admin` —el rol— y, además, bajo el permiso
/// por recurso y acción que exige cada endpoint: un token válido con rol ADMIN pero sin
/// el permiso concedido no pasa.
/// </summary>
public static class UsuariosAdminEndpoints
{
    private const string RutaBase = "/api/v1/admin/identity";

    public static IEndpointRouteBuilder MapUsuariosAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup(RutaBase)
            .WithTags("Gestión de usuarios")
            .WithOpenApi()
            .RequireAuthorization("admin");

        grupo.MapGet("/usuarios", ListarAsync)
            .WithName("ListarUsuariosPortal")
            .WithSummary("Listar los usuarios del portal con sus zonas, su nivel de acceso y su último acceso");

        return app;
    }

    private static async Task<IResult> ListarAsync(
        [FromQuery] string? busqueda,
        [FromQuery] string? tipo,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanoPagina,
        [FromServices] IGestionUsuariosService gestionUsuarios,
        [FromServices] IValidator<ConsultaUsuarios> validador,
        CancellationToken cancellationToken)
    {
        var consulta = ConsultaUsuarios.Crear(busqueda, tipo, pagina, tamanoPagina);

        // La forma del pedido primero; los permisos los verifica el filtro del endpoint,
        // que corre antes que este handler.
        var validacion = await validador.ValidateAsync(consulta, cancellationToken);
        if (!validacion.IsValid)
        {
            return Results.BadRequest(new AuthResponse(
                false,
                "Datos inválidos",
                validacion.Errors.Select(e => e.ErrorMessage)));
        }

        var filtrados = await gestionUsuarios.ListarAsync(consulta.Busqueda, consulta.TipoDeUsuario(), cancellationToken);

        // Los KPIs y las filas salen del MISMO conjunto filtrado: se calculan sobre
        // `filtrados` y recién después se recorta la página. Calcularlos sobre la tabla
        // completa mostraría un total que no cuadra con lo que se ve.
        var kpis = KpisUsuariosResponse.De(filtrados);

        var filas = filtrados
            .Skip((consulta.Pagina - 1) * consulta.TamanoPagina)
            .Take(consulta.TamanoPagina)
            .ToList();

        var totalPaginas = (int)Math.Ceiling(filtrados.Count / (double)consulta.TamanoPagina);

        return Results.Ok(new ListadoUsuariosResponse(
            filas,
            kpis,
            consulta.Pagina,
            consulta.TamanoPagina,
            totalPaginas));
    }
}
