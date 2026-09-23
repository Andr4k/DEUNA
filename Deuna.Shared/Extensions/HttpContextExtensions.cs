using Deuna.Shared.Security;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Deuna.Shared.Extensions;

/// <summary>
/// Extensiones para acceder fácilmente a la información del usuario autenticado desde HttpContext.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Obtiene el UserId del usuario autenticado (desde JwtMiddleware o ClaimsPrincipal).
    /// </summary>
    public static Guid? GetUserId(this HttpContext context)
    {
        // Primero intenta desde Items (poblado por JwtMiddleware)
        if (context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is Guid userId)
        {
            return userId;
        }

        // Fallback: desde ClaimsPrincipal (poblado por autenticación JWT estándar)
        var claim = context.User.FindFirst(ClaimTypes.NameIdentifier) ?? context.User.FindFirst("sub");
        if (claim != null && Guid.TryParse(claim.Value, out var parsedId))
        {
            return parsedId;
        }

        return null;
    }

    /// <summary>
    /// Obtiene el Role del usuario autenticado.
    /// </summary>
    public static string? GetRole(this HttpContext context)
    {
        if (context.Items.TryGetValue("Role", out var roleObj) && roleObj is string role)
        {
            return role;
        }

        var claim = context.User.FindFirst(ClaimTypes.Role) ?? context.User.FindFirst("role");
        return claim?.Value;
    }

    /// <summary>
    /// Obtiene el Email del usuario autenticado.
    /// </summary>
    public static string? GetEmail(this HttpContext context)
    {
        if (context.Items.TryGetValue("Email", out var emailObj) && emailObj is string email)
        {
            return email;
        }

        var claim = context.User.FindFirst(ClaimTypes.Email) ?? context.User.FindFirst("email");
        return claim?.Value;
    }

    /// <summary>
    /// Verifica si el usuario tiene un rol específico.
    /// </summary>
    public static bool HasRole(this HttpContext context, string role)
    {
        var userRole = context.GetRole();
        return string.Equals(userRole, role, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica si el usuario es Restaurant.
    /// </summary>
    public static bool IsRestaurant(this HttpContext context) => context.HasRole(Roles.Restaurant);

    /// <summary>
    /// Verifica si el usuario es Rider.
    /// </summary>
    public static bool IsRider(this HttpContext context) => context.HasRole(Roles.Rider);

    /// <summary>
    /// Verifica si el usuario es Admin.
    /// </summary>
    public static bool IsAdmin(this HttpContext context) => context.HasRole(Roles.Admin);
}
