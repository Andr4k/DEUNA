using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Orders.Service;
using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Events;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Deuna.Orders.Service.Tests.Features;

public class CrearPedidoTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CrearPedidoTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        // Token JWT real firmado con la misma clave que valida el servicio.
        // El literal "test-restaurant-token" que usaban los tests no es un token válido.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.CreateRestaurantToken(TestData.RestauranteId));
        return client;
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
    public async Task CrearPedido_WithValidData_Returns201WithPedidoCreado()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();
        using var client = CreateClient();

        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId,
            Items: new List<CrearItemPedidoRequest>
            {
                new("Hamburguesa Clásica", "Carne 180g, queso, lechuga, tomate", 2, 25000m),
                new("Papas Fritas", "Porción grande", 1, 12000m)
            },
            DireccionEntrega: new CrearDireccionEntregaRequest(
                Calle: "Calle 45",
                Numero: "#23-10",
                Interior: "Apto 301",
                Referencia: "Edificio Torres del Parque",
                Ciudad: "Bogotá",
                Departamento: "Cundinamarca",
                CodigoPostal: "110111",
                Latitud: 4.6097100m,
                Longitud: -74.0817500m
            ),
            Contactos: new List<CrearContactoPedidoRequest>
                                    {
                                        new("Cliente", "Juan Pérez", "+573001234567", "juan@email.com"),
                                        new("Restaurante", "Demo Restaurant", "+5712345678", "pedidos@demo.com")
                                    },
            NotasCliente: "Sin cebolla"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CrearPedidoResponse>();
        result.Should().NotBeNull();
        result!.PedidoId.Should().NotBeEmpty();
        result.Codigo.Should().StartWith("PED-");
        result.TokenQrLocal.Should().NotBeNullOrEmpty().And.HaveLength(32);
        result.Estado.Should().Be("Pendiente");
        result.Subtotal.Should().Be(62000m); // 2*25000 + 1*12000
        result.CostoEnvio.Should().BeGreaterThan(0);
        result.Total.Should().Be(result.Subtotal + result.CostoEnvio);
        result.FechaCreacion.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CrearPedido_WithInvalidRestaurant_Returns400()
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
    }

    [Fact]
    public async Task CrearPedido_WithEmptyItems_Returns400()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();
        using var client = CreateClient();

        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId,
            Items: new List<CrearItemPedidoRequest>(), // Vacío
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
    }

    [Fact]
    public async Task CrearPedido_WithNoClienteContact_Returns400()
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
                            new("Restaurante", "Demo Restaurant", "+5712345678", "pedidos@demo.com")
                            // Falta contacto tipo Cliente
                        }
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CrearPedido_WithInvalidCoordinates_Returns400()
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
                Latitud: 91m, // Inválida > 90
                Longitud: -200m // Inválida < -180
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
    }

    [Fact]
    public async Task CrearPedido_PersistsOrderWithAllSubtables()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();
        using var client = CreateClient();

        var request = new CrearPedidoRequest(
            RestauranteId: TestData.RestauranteId,
            Items: new List<CrearItemPedidoRequest>
            {
                new("Hamburguesa", "Desc", 2, 25000m),
                new("Papas", "Desc", 1, 12000m)
            },
            DireccionEntrega: new CrearDireccionEntregaRequest(
                Calle: "Calle 45", Numero: "#23-10", Interior: null, Referencia: null,
                Ciudad: "Bogotá", Departamento: "Cundinamarca", CodigoPostal: "110111",
                Latitud: 4.6097100m, Longitud: -74.0817500m
            ),
            Contactos: new List<CrearContactoPedidoRequest>
                        {
                            new("Cliente", "Juan Pérez", "+573001234567", "juan@email.com"),
                            new("Restaurante", "Demo Restaurant", "+5712345678", "pedidos@demo.com")
                        }
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CrearPedidoResponse>();
        var pedidoId = result!.PedidoId;

        // Assert - Verificar persistencia en BD
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var pedido = await db.Pedidos
            .Include(p => p.Items)
            .Include(p => p.Contactos)
            .Include(p => p.Tarifa)
            .Include(p => p.DireccionEntrega)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);

        pedido.Should().NotBeNull();
        pedido!.Items.Should().HaveCount(2);
        pedido.Contactos.Should().HaveCount(2);
        pedido.Tarifa.Should().NotBeNull();
        pedido.DireccionEntrega.Should().NotBeNull();
        pedido.DireccionEntrega.Ubicacion.Should().NotBeNull();
        pedido.Codigo.Should().StartWith("PED-");
        pedido.TokenQrLocal.Should().HaveLength(32);
        pedido.TokenQrEntrega.Should().HaveLength(32);
        // Son de partes distintas: el del local lo escanea el domiciliario, el de entrega el
        // cliente. Si pudieran coincidir, el QR del local no probaría nada (FR-002.5).
        pedido.TokenQrLocal.Should().NotBe(pedido.TokenQrEntrega);
    }
}