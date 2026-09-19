using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Deuna.Shared.Middleware;

/// <summary>
/// Middleware reutilizable para validación de JWT y extracción de claims de usuario.
/// Se ejecuta antes de la pipeline de autenticación/autorización estándar para poblar HttpContext.Items.
/// </summary>
public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtOptions _options;

    public JwtMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var token = ExtractToken(context);

        if (!string.IsNullOrEmpty(token))
        {
            AttachUserToContext(context, token);
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader["Bearer ".Length..].Trim();
    }

    private void AttachUserToContext(HttpContext context, string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_options.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = _options.ValidateIssuer,
                ValidIssuer = _options.Issuer,
                ValidateAudience = _options.ValidateAudience,
                ValidAudience = _options.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;

            var userIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier || x.Type == "sub");
            var roleClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Role || x.Type == "role");
            var emailClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Email || x.Type == "email");

            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                context.Items["UserId"] = userId;
            }

            if (roleClaim != null)
            {
                context.Items["Role"] = roleClaim.Value;
            }

            if (emailClaim != null)
            {
                context.Items["Email"] = emailClaim.Value;
            }

            // También adjuntar el principal para compatibilidad con [Authorize]
            var identity = new ClaimsIdentity(jwtToken.Claims, JwtBearerDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(identity);
        }
        catch (SecurityTokenExpiredException)
        {
            context.Items["JwtError"] = "Token expired";
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            context.Items["JwtError"] = "Invalid signature";
        }
        catch (SecurityTokenException)
        {
            context.Items["JwtError"] = "Invalid token";
        }
        catch
        {
            // Token inválido - continuar sin adjuntar contexto
        }
    }
}

/// <summary>
/// Opciones de configuración para JWT desde appsettings.json
/// </summary>
public class JwtOptions
{
    public string SecretKey { get; set; } = "your-super-secret-key-at-least-32-characters-long";
    public string Issuer { get; set; } = "deuna-api";
    public string Audience { get; set; } = "deuna-clients";
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
}
