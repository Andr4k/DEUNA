using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Deuna.Shared.Middleware;
using Deuna.Shared.Security;
using System.Security.Claims;
using System.Text;

namespace Deuna.Shared.Extensions;

/// <summary>
/// Extensiones para registrar autenticación JWT y el middleware compartido en servicios.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Agrega autenticación JWT Bearer y el middleware JwtMiddleware a la pipeline.
    /// </summary>
    /// <param name="services">Colección de servicios</param>
    /// <param name="configuration">Configuración (sección "Jwt")</param>
    /// <returns>Services para chaining</returns>
    public static IServiceCollection AddDeunaJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var secretKey = jwtSection["SecretKey"] ?? "your-super-secret-key-at-least-32-characters-long";
        var issuer = jwtSection["Issuer"] ?? "deuna-api";
        var audience = jwtSection["Audience"] ?? "deuna-clients";
        var validateIssuer = jwtSection.GetValue("ValidateIssuer", true);
        var validateAudience = jwtSection.GetValue("ValidateAudience", true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = validateIssuer,
                    ValidateAudience = validateAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.Zero,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("restaurant", policy => policy.RequireRole(Roles.Restaurant));
            options.AddPolicy("rider", policy => policy.RequireRole(Roles.Rider));
            options.AddPolicy("admin", policy => policy.RequireRole(Roles.Admin));
            options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
        });

        return services;
    }

    /// <summary>
    /// Agrega el middleware JWT compartido a la pipeline HTTP.
    /// Debe llamarse DESPUÉS de UseAuthentication() y UseAuthorization().
    /// </summary>
    /// <param name="app">Application builder</param>
    /// <returns>App para chaining</returns>
    public static IApplicationBuilder UseDeunaJwtMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<JwtMiddleware>();
    }
}
