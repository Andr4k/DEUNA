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
    private readonly HttpClient _client;

    public RegisterTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static RegisterRestaurantRequest RequestRestaurante(string? email = null) => new(
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
        Longitud: -74.0817m);

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
        var (codigo, data) = await RegistrarAsync(RequestRestaurante());

        codigo.Should().Be(HttpStatusCode.OK);
        data!.Value.GetProperty("verificationToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.Value.GetProperty("role").GetString().Should().Be("RESTAURANT");
    }

    [Fact]
    public async Task VerificarEmail_ConElTokenDelRegistro_ConfirmaLaCuenta()
    {
        var (_, data) = await RegistrarAsync(RequestRestaurante());
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
        var request = RequestRestaurante();
        await RegistrarAsync(request);

        var response = await _client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("message").GetString().Should().Contain("ya está registrado");
    }
}
