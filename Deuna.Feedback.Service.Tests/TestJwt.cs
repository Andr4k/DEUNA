using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Shared.Security;
using Microsoft.IdentityModel.Tokens;

namespace Deuna.Feedback.Service.Tests;

/// <summary>
/// Emite JWT reales firmados con la misma clave, issuer y audience que valida el servicio.
///
/// Un token inventado ("test-admin-token") no es un token: el pipeline lo rechaza con 401 y el
/// test fallaría por una razón que no tiene nada que ver con lo que prueba.
/// </summary>
internal static class TestJwt
{
    public const string SecretKey = "TestSecretKeyForTestingPurposesOnly123456789";
    public const string Issuer = "Deuna.Test";
    public const string Audience = "Deuna.Test";

    /// <summary>
    /// Token con rol ADMIN, que es el que exige la política "admin" del panel.
    /// </summary>
    public static string CreateAdminToken(Guid adminId) =>
        CreateToken(adminId, Roles.Admin, "admin@test.com");

    /// <summary>
    /// Token con rol RESTAURANT: sirve para enviar encuestas por /api/v1/feedback, y no para
    /// leer el panel. Es el token con el que se comprueba que el grupo nuevo sí separa permisos.
    /// </summary>
    public static string CreateRestaurantToken(Guid restauranteId) =>
        CreateToken(restauranteId, Roles.Restaurant, "restaurante@test.com");

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
