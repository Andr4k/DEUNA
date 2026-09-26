using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Deuna.Identity.Service.Services;

namespace Deuna.Identity.Service.Endpoints;

/// <summary>
/// Exige un permiso por recurso y acción antes de ejecutar el endpoint.
///
/// Es una capa distinta de las otras dos que ya estaban: el validador mira la FORMA del
/// pedido y la política de rol mira quién entra; esto mira si el usuario PUEDE. Un token
/// válido con el rol correcto y un pedido perfectamente bien formado —incluso uno para
/// concederse todos los permisos— no pasa si la acción no está concedida sobre el recurso.
/// Es lo que hace que un token robado no alcance los endpoints que su dueño tiene limitados.
///
/// Falla en cerrado en todos sus caminos: sin usuario identificable, y sin fila concedida y
/// vigente, la respuesta es 403.
/// </summary>
public sealed class PermisoEndpointFilter : IEndpointFilter
{
    private readonly string _recurso;
    private readonly string _accion;

    public PermisoEndpointFilter(string recurso, string accion)
    {
        _recurso = recurso;
        _accion = accion;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var usuarioId = UsuarioDelToken(context.HttpContext.User);

        if (usuarioId is null)
        {
            // Un principal sin identificable no tiene permiso que consultar. Pasa de largo
            // sería fail-open; no pasa.
            return Results.Forbid();
        }

        var permisos = context.HttpContext.RequestServices.GetRequiredService<IPermisoService>();

        var concedido = await permisos.TienePermisoAsync(
            usuarioId.Value,
            _recurso,
            _accion,
            context.HttpContext.RequestAborted);

        if (!concedido)
        {
            return Results.Forbid();
        }

        return await next(context);
    }

    /// <summary>Id del usuario autenticado, tolerando el mapeo de claims entrantes.</summary>
    private static Guid? UsuarioDelToken(ClaimsPrincipal user)
    {
        var valor = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(valor, out var id) ? id : null;
    }
}

/// <summary>Encadena la exigencia del permiso al endpoint que la declara.</summary>
public static class EndpointPermisoExtensions
{
    /// <summary>
    /// Declara el recurso y la acción que el endpoint exige. Se escribe al lado de la ruta
    /// para que la exigencia se lea donde se declara el endpoint, y no en un atributo lejos
    /// de su ruta.
    /// </summary>
    /// <param name="endpoint">El endpoint a proteger.</param>
    /// <param name="recurso">La clave del recurso, la misma del catálogo `recursos`.</param>
    /// <param name="accion">La acción exigida (READ, UPDATE, ...).</param>
    public static RouteHandlerBuilder RequierePermiso(
        this RouteHandlerBuilder endpoint,
        string recurso,
        string accion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recurso);
        ArgumentException.ThrowIfNullOrWhiteSpace(accion);

        return endpoint.AddEndpointFilter(new PermisoEndpointFilter(recurso, accion));
    }
}
