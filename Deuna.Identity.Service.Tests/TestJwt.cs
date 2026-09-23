using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Shared.Security;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Identity.Service.Tests;

/// <summary>
/// Emite JWT reales firmados con la misma clave que valida el servicio.
/// El alta de un administrador no tiene endpoint público, así que para probar la
/// emisión de códigos de autorización el token se firma directamente.
/// </summary>
internal static class TestJwt
{
    private const string SecretKey = "TestSecretKeyForTestingPurposesOnly123456789";
    private const string Issuer = "Deuna.Test";
    private const string Audience = "Deuna.Test";

    public static string CreateAdminToken() =>
        CreateToken(Guid.NewGuid(), Roles.Admin, "admin@deuna.test");

    public static string CreateRestaurantToken() =>
        CreateToken(Guid.NewGuid(), Roles.Restaurant, "restaurant@deuna.test");

    private static string CreateToken(Guid subject, string role, string email)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject.ToString()),
                new Claim("role", role),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
