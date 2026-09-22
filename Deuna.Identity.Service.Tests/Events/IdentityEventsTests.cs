using System;
using System.Linq;
using System.Threading.Tasks;
using Deuna.Identity.Service;
using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Events;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Services;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Deuna.Identity.Service.Tests.Events;

public class IdentityEventsTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly InMemoryTestHarness _harness;
    private IServiceProvider? _provider;

    public IdentityEventsTests()
    {
        _harness = new InMemoryTestHarness();
        
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<IdentityDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    // Add InMemory database for testing
                    services.AddDbContext<IdentityDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("TestIdentityDb");
                    });

                    // Replace MassTransit with InMemoryTestHarness
                    var massTransitDescriptors = services.Where(d => 
                        d.ServiceType.FullName?.Contains("MassTransit") == true ||
                        d.ImplementationType?.FullName?.Contains("MassTransit") == true
                    ).ToList();
                    foreach (var d in massTransitDescriptors) services.Remove(d);
                    
                    services.AddMassTransit(x =>
                    {
                        x.UsingInMemory((context, cfg) =>
                        {
                            cfg.ConfigureEndpoints(context);
                        });
                    });
                    services.AddSingleton(_harness);
                    services.AddScoped<IEventPublisher, EventPublisher>();
                });
            });
    }

    public async Task InitializeAsync()
    {
        await _harness.Start();
        _provider = _factory.Services;
        
        // Ensure database is created
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        _factory.Dispose();
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task RegisterRestaurant_PublishesSingleUsuarioRegistradoEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRestaurantRequest(
            Email: "test@restaurant.com",
            Password: "ValidPass123!",
            FirstName: "Test",
            LastName: "Restaurant",
            PhoneNumber: "+57300123456",
            RazonSocial: "Test Restaurant S.A.S.",
            NombreComercial: "Test Restaurant",
            Nit: "900123456-7",
            DireccionSede: "Calle 123 #45-67",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);

        // Assert - HTTP response
        response.EnsureSuccessStatusCode();

        // Assert - Event published exactly once
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);

        var evento = publishedList[0].Context.Message;
        Assert.Equal("Restaurant", evento.Rol);
        Assert.Equal("test@restaurant.com", evento.Email);
        Assert.Equal("+57300123456", evento.Telefono);
        Assert.True(evento.Activo);
        Assert.NotEqual(default, evento.UsuarioId);
        Assert.True(evento.OccurredAt <= DateTime.UtcNow);
        Assert.True(evento.OccurredAt >= DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task RegisterRider_PublishesSingleUsuarioRegistradoEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRiderRequest(
            Email: "rider@test.com",
            Password: "ValidPass123!",
            FirstName: "Test",
            LastName: "Rider",
            PhoneNumber: "+57300987654",
            NombreCompleto: "Test Rider",
            DocumentoIdentidad: "1234567890",
            CiudadOperacion: "Bogotá",
            FotoPerfilUrl: null
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/register/rider", request);

        // Assert - HTTP response
        response.EnsureSuccessStatusCode();

        // Assert - Event published exactly once
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);

        var evento = publishedList[0].Context.Message;
        Assert.Equal("Courier", evento.Rol);
        Assert.Equal("rider@test.com", evento.Email);
        Assert.Equal("+57300987654", evento.Telefono);
        Assert.True(evento.Activo);
        Assert.NotEqual(default, evento.UsuarioId);
    }

    [Fact]
    public async Task RegisterAsync_PublishesSingleUsuarioRegistradoEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRequest(
            Email: "admin@test.com",
            Password: "ValidPass123!",
            FirstName: "Test",
            LastName: "Admin",
            PhoneNumber: "+57300111222",
            Role: "Admin"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/register", request);

        // Assert - HTTP response
        response.EnsureSuccessStatusCode();

        // Assert - Event published exactly once
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);

        var evento = publishedList[0].Context.Message;
        Assert.Equal("Admin", evento.Rol);
        Assert.Equal("admin@test.com", evento.Email);
        Assert.Equal("+57300111222", evento.Telefono);
        Assert.True(evento.Activo);
        Assert.NotEqual(default, evento.UsuarioId);
    }

    [Fact]
    public async Task RegisterRestaurant_DuplicateEmail_DoesNotPublishEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRestaurantRequest(
            Email: "duplicate@test.com",
            Password: "ValidPass123!",
            FirstName: "Test",
            LastName: "Restaurant",
            PhoneNumber: "+57300123456",
            RazonSocial: "Test Restaurant S.A.S.",
            NombreComercial: "Test Restaurant",
            Nit: "900123456-7",
            DireccionSede: "Calle 123 #45-67",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m
        );

        // Act - First registration succeeds
        var response1 = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        response1.EnsureSuccessStatusCode();

        // Second registration fails
        var response2 = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        
        // Assert - Second request fails
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response2.StatusCode);

        // Assert - Only ONE event published (from first successful registration)
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);
    }

    [Fact]
    public async Task RegisterRestaurant_InvalidData_DoesNotPublishEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRestaurantRequest(
            Email: "invalid-email", // Invalid email format
            Password: "ValidPass123!",
            FirstName: "Test",
            LastName: "Restaurant",
            PhoneNumber: "+57300123456",
            RazonSocial: "Test Restaurant S.A.S.",
            NombreComercial: "Test Restaurant",
            Nit: "900123456-7",
            DireccionSede: "Calle 123 #45-67",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);

        // Assert - Request fails validation
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        // Assert - No event published
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Empty(publishedList);
    }

    [Fact]
    public async Task RegisterRestaurant_DuplicateNit_DoesNotPublishEvent()
    {
        // Arrange
        using var client = CreateClient();
        var request1 = new RegisterRestaurantRequest(
            Email: "first@test.com",
            Password: "ValidPass123!",
            FirstName: "First",
            LastName: "Restaurant",
            PhoneNumber: "+57300111111",
            RazonSocial: "First Restaurant S.A.S.",
            NombreComercial: "First Restaurant",
            Nit: "900123456-7",
            DireccionSede: "Calle 1 #1-1",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m
        );

        var request2 = new RegisterRestaurantRequest(
            Email: "second@test.com",
            Password: "ValidPass123!",
            FirstName: "Second",
            LastName: "Restaurant",
            PhoneNumber: "+57300222222",
            RazonSocial: "Second Restaurant S.A.S.",
            NombreComercial: "Second Restaurant",
            Nit: "900123456-7", // Same NIT
            DireccionSede: "Calle 2 #2-2",
            Ciudad: "Medellín",
            Latitud: 6.2442m,
            Longitud: -75.5812m
        );

        // Act
        var response1 = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request1);
        response1.EnsureSuccessStatusCode();

        var response2 = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request2);

        // Assert - Second request fails
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response2.StatusCode);

        // Assert - Only ONE event published (from first successful registration)
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);
    }

    [Fact]
    public async Task EventContent_ExcludesSensitiveData()
    {
        // Arrange
        using var client = CreateClient();
        var request = new RegisterRestaurantRequest(
            Email: "secure@test.com",
            Password: "ValidPass123!",
            FirstName: "Secure",
            LastName: "Restaurant",
            PhoneNumber: "+57300333333",
            RazonSocial: "Secure Restaurant S.A.S.",
            NombreComercial: "Secure Restaurant",
            Nit: "900999999-9",
            DireccionSede: "Calle 3 #3-3",
            Ciudad: "Bogotá",
            Latitud: 4.6097m,
            Longitud: -74.0817m
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/identity/register/restaurant", request);
        response.EnsureSuccessStatusCode();

        // Assert - Event content doesn't contain sensitive data
        var published = _harness.Published.Select<UsuarioRegistrado>();
        var publishedList = published.ToList();
        Assert.Single(publishedList);

        var evento = publishedList[0].Context.Message;
        
        // Verify prohibited fields are NOT present
        var json = System.Text.Json.JsonSerializer.Serialize(evento);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jwt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hash", json, StringComparison.OrdinalIgnoreCase);
        
        // Verify required fields ARE present
        Assert.Contains("UsuarioId", json);
        Assert.Contains("Rol", json);
        Assert.Contains("Email", json);
        Assert.Contains("Telefono", json);
        Assert.Contains("Activo", json);
        Assert.Contains("OccurredAt", json);
    }
}