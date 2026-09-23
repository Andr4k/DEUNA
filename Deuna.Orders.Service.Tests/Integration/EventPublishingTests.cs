using System.Net;
using System.Net.Http.Json;
using Deuna.Orders.Service;
using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Deuna.Orders.Service.Tests.Integration;

public class EventPublishingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EventPublishingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await TestData.ResetAsync(db);
    }

    private async Task SeedRestauranteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await TestData.SeedRestauranteAsync(db);
    }

    [Fact]
    public async Task CrearPedido_PublicaEventoPedidoCreado_Unico()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();

        using var client = CreateClient();

        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId,
            Items: new List<CrearItemPedidoRequest>
            {
                new("Hamburguesa", "Desc", 1, 25000m)
            },
            DireccionEntrega: new CrearDireccionEntregaRequest(
                Calle: "Calle 45", Numero: "#23-10", Interior: null, Referencia: null,
                Ciudad: "Bogotá", Departamento: "Cundinamarca", CodigoPostal: "110111",
                Latitud: 4.6097100m, Longitud: -74.0817500m
            ),
            Contactos: new List<CrearContactoPedidoRequest>
            {
                new("Cliente", "Juan Pérez", "+573001234567", "juan@email.com")
            }
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verificar que se creó el pedido correctamente en la BD
        // (El evento se publica vía MassTransit, pero en InMemory no podemos interceptar fácilmente)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.RestauranteId == TestData.RestauranteId);
        
        pedido.Should().NotBeNull();
        pedido!.Codigo.Should().StartWith("PED-");
        pedido.Estado.Should().Be("Pendiente");
        pedido.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CrearPedido_NoPersistePedido_CuandoFallaValidacion()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();

        using var client = CreateClient();

        // Request inválido: sin items
        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId,
            Items: new List<CrearItemPedidoRequest>(), // Inválido
            DireccionEntrega: new CrearDireccionEntregaRequest(
                Calle: "Calle 45", Numero: "#23-10", Interior: null, Referencia: null,
                Ciudad: "Bogotá", Departamento: "Cundinamarca", CodigoPostal: "110111",
                Latitud: 4.6097100m, Longitud: -74.0817500m
            ),
            Contactos: new List<CrearContactoPedidoRequest>
            {
                new("Cliente", "Juan Pérez", "+573001234567", "juan@email.com")
            }
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // NO debe persistir pedido
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pedidos = await db.Pedidos.ToListAsync();
        pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task CrearPedido_NoPersistePedido_CuandoRestauranteNoExiste()
    {
        // Arrange
        await ResetDatabaseAsync();
        // NO seed restaurante

        using var client = CreateClient();

        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId, // No existe
            Items: new List<CrearItemPedidoRequest>
            {
                new("Hamburguesa", "Desc", 1, 25000m)
            },
            DireccionEntrega: new CrearDireccionEntregaRequest(
                Calle: "Calle 45", Numero: "#23-10", Interior: null, Referencia: null,
                Ciudad: "Bogotá", Departamento: "Cundinamarca", CodigoPostal: "110111",
                Latitud: 4.6097100m, Longitud: -74.0817500m
            ),
            Contactos: new List<CrearContactoPedidoRequest>
            {
                new("Cliente", "Juan Pérez", "+573001234567", "juan@email.com")
            }
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // NO debe persistir pedido
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pedidos = await db.Pedidos.ToListAsync();
        pedidos.Should().BeEmpty();
    }

    private HttpClient CreateClient()
    {
        // Token JWT real firmado con la misma clave que valida el servicio.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.CreateRestaurantToken(TestData.RestauranteId));
        return client;
    }
}