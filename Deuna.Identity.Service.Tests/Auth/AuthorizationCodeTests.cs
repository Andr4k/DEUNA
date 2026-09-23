using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Identity.Service.Tests.Auth;

/// <summary>
/// Alta de restaurante en dos pasos (FR-001.8 a FR-001.11).
///
/// El restaurante llega por un proceso comercial previo: el área de ventas lo contacta y
/// le entrega un código de autorización de un solo uso. Primero se valida el código y se
/// obtiene un token de continuidad; recién entonces se envían los datos. Sin código válido
/// no se crea la cuenta (política anti-spam).
/// </summary>
public class AuthorizationCodeTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthorizationCodeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateAdminToken());
        return client;
    }

    private HttpClient CreateRestaurantClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateRestaurantToken());
        return client;
    }

    private async Task<HttpResponseMessage> EmitirCodigoAsync(string? notas = null)
    {
        using var admin = CreateAdminClient();
        return await admin.PostAsJsonAsync("/api/v1/identity/authorization-codes",
            new EmitAuthorizationCodeRequest(notas));
    }

    /// <summary>Emite un código y devuelve el string del código.</summary>
    private async Task<string> EmitirCodigoValidoAsync()
    {
        var response = await EmitirCodigoAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "el área comercial debe poder emitir códigos");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("codigo").GetString()!;
    }

    private async Task<HttpResponseMessage> ValidarCodigoAsync(string codigo)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/v1/identity/register/restaurant/validate-code",
            new ValidateAuthorizationCodeRequest(codigo));
    }

    /// <summary>Valida un código y devuelve el token de continuidad.</summary>
    private async Task<string> ObtenerTokenDeContinuidadAsync(string codigo)
    {
        var response = await ValidarCodigoAsync(codigo);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "un código válido debe habilitar el registro");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("continuidadToken").GetString()!;
    }

    private static RegisterRestaurantRequest RequestRestaurante(string? continuidadToken) => new(
        Email: $"resto-{Guid.NewGuid():N}@deuna.test",
        Password: "Deuna2026*",
        FirstName: "Andres",
        LastName: "Resto",
        PhoneNumber: null,
        RazonSocial: "Codigo Test SAS",
        NombreComercial: "Codigo Test",
        Nit: $"900{Guid.NewGuid():N}"[..10],
        DireccionSede: "Calle 45 #23-10",
        Ciudad: "Bogota",
        Latitud: 4.6097m,
        Longitud: -74.0817m,
        ContinuidadToken: continuidadToken!);

    private async Task<HttpResponseMessage> RegistrarAsync(RegisterRestaurantRequest request) =>
        await _client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);

    /// <summary>Fuerza el vencimiento de un código para probar el rechazo por vigencia.</summary>
    private async Task VencerCodigoAsync(string codigo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entidad = await db.CodigosAutorizacion.FirstAsync(c => c.Codigo == codigo);
        entidad.FechaVencimiento = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    // ---------- emisión (área comercial) ----------

    [Fact]
    public async Task EmitirCodigo_ConRolAdmin_DevuelveUnCodigoDisponible()
    {
        var response = await EmitirCodigoAsync("Restaurante La Esquina");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = json.GetProperty("data");

        data.GetProperty("codigo").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("estado").GetString().Should().Be("Disponible");
        data.GetProperty("fechaVencimiento").GetDateTime().Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task EmitirCodigo_SinToken_NoAutoriza()
    {
        using var anonimo = _factory.CreateClient();

        var response = await anonimo.PostAsJsonAsync("/api/v1/identity/authorization-codes",
            new EmitAuthorizationCodeRequest(null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmitirCodigo_ConRolRestaurante_Prohibido()
    {
        using var restaurante = CreateRestaurantClient();

        var response = await restaurante.PostAsJsonAsync("/api/v1/identity/authorization-codes",
            new EmitAuthorizationCodeRequest(null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- validación del código ----------

    [Fact]
    public async Task ValidarCodigo_Valido_DevuelveTokenDeContinuidad()
    {
        var codigo = await EmitirCodigoValidoAsync();

        var response = await ValidarCodigoAsync(codigo);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = json.GetProperty("data");
        data.GetProperty("continuidadToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("codigoId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidarCodigo_Inexistente_DevuelveError()
    {
        var response = await ValidarCodigoAsync("CODIGO-QUE-NO-EXISTE");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("message").GetString().Should().Contain("no es válido");
    }

    [Fact]
    public async Task ValidarCodigo_Vencido_DevuelveError()
    {
        var codigo = await EmitirCodigoValidoAsync();
        await VencerCodigoAsync(codigo);

        var response = await ValidarCodigoAsync(codigo);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("message").GetString().Should().Contain("venció");
    }

    [Fact]
    public async Task ValidarCodigo_YaUsado_DevuelveError()
    {
        var codigo = await EmitirCodigoValidoAsync();
        await ObtenerTokenDeContinuidadAsync(codigo); // primer uso

        var response = await ValidarCodigoAsync(codigo);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("message").GetString().Should().Contain("ya fue utilizado");
    }

    // ---------- registro con token de continuidad ----------

    [Fact]
    public async Task Registro_SinTokenDeContinuidad_DevuelveError()
    {
        var response = await RegistrarAsync(RequestRestaurante(null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Registro_ConTokenDeContinuidadInvalido_DevuelveError()
    {
        var response = await RegistrarAsync(RequestRestaurante("token-inventado"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Registro_ConTokenDeContinuidad_CreaLaCuenta()
    {
        var codigo = await EmitirCodigoValidoAsync();
        var token = await ObtenerTokenDeContinuidadAsync(codigo);
        var request = RequestRestaurante(token);

        var response = await RegistrarAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("data").GetProperty("role").GetString().Should().Be("RESTAURANT");

        // El código queda ligado al alta: es la trazabilidad comercial del registro.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entidad = await db.CodigosAutorizacion.FirstAsync(c => c.Codigo == codigo);
        entidad.Estado.Should().Be("Usado");
        entidad.RestauranteId.Should().NotBeNull("el código debe quedar asociado al restaurante que lo usó");
        entidad.FechaUso.Should().NotBeNull();
    }

    [Fact]
    public async Task CodigoDeUnSoloUso_ElSegundoRegistroNoSeCompleta()
    {
        var codigo = await EmitirCodigoValidoAsync();
        var token = await ObtenerTokenDeContinuidadAsync(codigo);

        var primero = await RegistrarAsync(RequestRestaurante(token));
        primero.StatusCode.Should().Be(HttpStatusCode.OK);

        // Un segundo restaurante intenta usar el mismo token de continuidad.
        var segundo = await RegistrarAsync(RequestRestaurante(token));

        segundo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await segundo.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }
}
