using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Identity.Service.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Deuna.Identity.Service.Tests.Auth;

/// <summary>
/// Flujo de alta de restaurante y verificación de email.
/// Cubre el defecto por el que el token de verificación se generaba y se descartaba,
/// dejando la verificación de email imposible de completar.
/// </summary>
public class RegisterTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwt.CreateAdminToken());
        return client;
    }

    /// <summary>
    /// Emite un código de autorización y lo valida, como hace el área comercial con el
    /// restaurante antes de que este envíe sus datos (FR-001.8 a FR-001.10).
    /// </summary>
    private async Task<string> ObtenerTokenDeContinuidadAsync()
    {
        using var admin = CreateAdminClient();
        var emision = await admin.PostAsJsonAsync("/api/v1/identity/authorization-codes",
            new EmitAuthorizationCodeRequest(null));
        var emisionJson = await emision.Content.ReadFromJsonAsync<JsonElement>();
        var codigo = emisionJson.GetProperty("data").GetProperty("codigo").GetString()!;

        using var client = _factory.CreateClient();
        var validacion = await client.PostAsJsonAsync(
            "/api/v1/identity/register/restaurant/validate-code",
            new ValidateAuthorizationCodeRequest(codigo));
        var validacionJson = await validacion.Content.ReadFromJsonAsync<JsonElement>();
        return validacionJson.GetProperty("data").GetProperty("continuidadToken").GetString()!;
    }

    private async Task<RegisterRestaurantRequest> RequestRestauranteAsync(string? email = null) => new(
        Email: email ?? $"resto-{Guid.NewGuid():N}@deuna.test",
        Password: "Deuna2026*",
        FirstName: "Andres",
        LastName: "Resto",
        PhoneNumber: null,
        RazonSocial: "Registro Test SAS",
        NombreComercial: "Registro Test",
        Nit: $"900{Guid.NewGuid():N}"[..10],
        DireccionSede: "Calle 45 #23-10",
        Ciudad: "Bogota",
        Latitud: 4.6097m,
        Longitud: -74.0817m,
        ContinuidadToken: await ObtenerTokenDeContinuidadAsync());

    private async Task<(HttpStatusCode Codigo, JsonElement? Data)> RegistrarAsync(RegisterRestaurantRequest request)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = json.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null ? d : (JsonElement?)null;
        return (response.StatusCode, data);
    }

    [Fact]
    public async Task Registro_DevuelveTokenDeVerificacion()
    {
        var (codigo, data) = await RegistrarAsync(await RequestRestauranteAsync());

        codigo.Should().Be(HttpStatusCode.OK);
        data!.Value.GetProperty("verificationToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.Value.GetProperty("role").GetString().Should().Be("RESTAURANT");
    }

    [Fact]
    public async Task VerificarEmail_ConElTokenDelRegistro_ConfirmaLaCuenta()
    {
        var (_, data) = await RegistrarAsync(await RequestRestauranteAsync());
        var token = data!.Value.GetProperty("verificationToken").GetString();

        var response = await _client.PostAsJsonAsync("/api/v1/identity/verify-email", new VerifyEmailRequest(token!));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task VerificarEmail_ConTokenInvalido_DevuelveError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/identity/verify-email",
            new VerifyEmailRequest("token-que-no-existe"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registro_ConEmailDuplicado_DevuelveErrorYNoCreaOtroUsuario()
    {
        var email = $"resto-{Guid.NewGuid():N}@deuna.test";

        // Primer alta, con su propio código de autorización.
        var primero = await RegistrarAsync(await RequestRestauranteAsync(email));
        primero.Codigo.Should().Be(HttpStatusCode.OK);

        // Segundo intento con un código nuevo pero el mismo email: el rechazo debe ser por
        // el email duplicado. Reusar el token anterior fallaría antes, por código ya usado.
        var response = await _client.PostAsJsonAsync("/api/v1/identity/register/restaurant",
            await RequestRestauranteAsync(email));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("message").GetString().Should().Contain("ya está registrado");
    }
}
