using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Delivery.Service.Tests.Tracking;

/// <summary>
/// Arranca Delivery Service con EF InMemory para la BD y un Redis real en Docker
/// (TestContainers) para el tracking. La configuración se inyecta por variables de
/// entorno porque Program.cs lee los flags en las sentencias de nivel superior.
/// </summary>
public class TrackingWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string JwtSecretKey = "ClaveDePruebasParaTrackingConMasDe32Caracteres!";
    public const string JwtIssuer = "Deuna.Identity";
    public const string JwtAudience = "Deuna.Api";

    private readonly string _redisConnectionString;

    public TrackingWebApplicationFactory(string redisConnectionString)
    {
        _redisConnectionString = redisConnectionString;

        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "InMemory");
        Environment.SetEnvironmentVariable("Redis__Configuration", redisConnectionString);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtSecretKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable("UseInMemoryDatabase", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("Redis__Configuration", null);
            Environment.SetEnvironmentVariable("Jwt__SecretKey", null);
            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
        }
    }

    /// <summary>Token JWT equivalente al que emite Identity para un repartidor.</summary>
    public static string CrearToken(Guid riderId, string rol = Roles.Rider, string? emisor = null, string? audiencia = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, riderId.ToString()),
            new(ClaimTypes.Role, rol),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: emisor ?? JwtIssuer,
            audience: audiencia ?? JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
