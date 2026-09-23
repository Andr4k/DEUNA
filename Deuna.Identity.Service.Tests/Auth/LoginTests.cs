using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BCrypt.Net;
using Deuna.Identity.Service;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Deuna.Shared.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Deuna.Identity.Service.Tests.Auth;

public class LoginTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoginTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Emite un código de autorización y lo valida, como hace el área comercial antes de
    /// que el restaurante envíe sus datos. Sin código válido no hay registro (FR-001.8).
    /// </summary>
    private async Task<string> ObtenerTokenDeContinuidadAsync()
    {
        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwt.CreateAdminToken());
        var emision = await admin.PostAsJsonAsync("/api/v1/identity/authorization-codes",
            new EmitAuthorizationCodeRequest(null));
        var emisionJson = await emision.Content.ReadFromJsonAsync<JsonElement>();
        var codigo = emisionJson.GetProperty("data").GetProperty("codigo").GetString()!;

        using var anonimo = _factory.CreateClient();
        var validacion = await anonimo.PostAsJsonAsync(
            "/api/v1/identity/register/restaurant/validate-code",
            new ValidateAuthorizationCodeRequest(codigo));
        var validacionJson = await validacion.Content.ReadFromJsonAsync<JsonElement>();
        return validacionJson.GetProperty("data").GetProperty("continuidadToken").GetString()!;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<Guid> RegisterRestaurantAsync(HttpClient client, string email, string password)
    {
        var request = new RegisterRestaurantRequest(
            Email: email,
            Password: password,
            FirstName: "Test",
            LastName: "Restaurant",
            PhoneNumber: "+57300123456",
            RazonSocial: "Test Restaurant S.A.S.",
            NombreComercial: "Test Restaurant",
            Nit: "900123456-7",
            DireccionSede: "Calle 123 #45-67",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m,
            ContinuidadToken: await ObtenerTokenDeContinuidadAsync()
        );

        var response = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResponse<RegisterResponse>>();
        result!.Success.Should().BeTrue();
        return result.Data!.UserId;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwtToken()
    {
        // Arrange - reset DB for isolation
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var email = "test@restaurant.com";
        var password = "ValidPass123!";
        await RegisterRestaurantAsync(client, email, password);

        var request = new LoginRequest(email, password, false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResponse<LoginResponse>>();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.AccessToken.Should().NotBeNullOrEmpty();
        result.Data.RefreshToken.Should().NotBeNullOrEmpty();
        result.Data.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Data.UserId.Should().NotBeEmpty();
        result.Data.Email.Should().Be(email);
        result.Data.Role.Should().Be(Roles.Restaurant);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var email = "test2@restaurant.com";
        var password = "ValidPass123!";
        await RegisterRestaurantAsync(client, email, password);

        var request = new LoginRequest(email, "WrongPassword!", false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Unauthorized() returns empty body, so we can't deserialize
        // Just verify the status code
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401WithGenericMessage()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var request = new LoginRequest("nonexistent@restaurant.com", "AnyPassword123!", false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Unauthorized() returns empty body
    }

    [Fact]
    public async Task Login_WithInactiveAccount_Returns403()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var email = "inactive@restaurant.com";
        var password = "ValidPass123!";
        var userId = await RegisterRestaurantAsync(client, email, password);

        // Deactivate user directly in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Usuarios.FirstAsync(u => u.Id == userId);
        user.IsActive = false;
        await db.SaveChangesAsync();

        var request = new LoginRequest(email, password, false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert - spec says 403 Forbidden for inactive account
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_UpdatesLastLoginAt()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var email = "test3@restaurant.com";
        var password = "ValidPass123!";
        var userId = await RegisterRestaurantAsync(client, email, password);

        var request = new LoginRequest(email, password, false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Usuarios.FirstAsync(u => u.Id == userId);
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Login_ReturnsTokenWithCorrectExpiration()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var email = "test4@restaurant.com";
        var password = "ValidPass123!";
        await RegisterRestaurantAsync(client, email, password);

        var request = new LoginRequest(email, password, false);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResponse<LoginResponse>>();
        result!.Success.Should().BeTrue();

        // Token should expire in ~8 hours (480 minutes) per spec
        var expectedExpiry = DateTime.UtcNow.AddHours(8);
        result.Data!.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(5));
    }
}

// Helper classes for deserialization
internal record AuthResponse<T>(bool Success, string Message, T? Data = default) where T : class;
internal record RegisterResponse(Guid UserId, string Email, string Role, string Message);
internal record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, Guid UserId, string Email, string Role, string FirstName, string LastName);