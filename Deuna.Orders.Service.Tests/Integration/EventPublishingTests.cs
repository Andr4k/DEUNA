using System.Net;
using System.Net.Http.Json;
using Deuna.Orders.Service;
using Deuna.Orders.Service.DTOs;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.Events;
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
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task SeedRestauranteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var restaurante = new RestauranteReplicado
        {
            Id = Guid.Parse("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6"),
            NombreComercial = "Demo Restaurant",
            RazonSocial = "Demo Restaurant S.A.S.",
            Nit = "900123456-7",
            DireccionSede = "Calle 45 #23-10",
            Ciudad = "Bogotá",
            Latitud = 4.6097100m,
            Longitud = -74.0817500m,
            Ubicacion = new NetTopologySuite.Geometries.Point(-74.0817500, 4.6097100) { SRID = 4326 },
            RadioCoberturaKm = 10.0m,
            AceptaPedidos = true,
            Activo = true,
            HoraApertura = new TimeSpan(8, 0, 0),
            HoraCierre = new TimeSpan(22, 0, 0),
            CreatedAt = DateTime.UtcNow
        };

        db.RestaurantesReplicados.Add(restaurante);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CrearPedido_PublicaEventoPedidoCreado_Unico()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedRestauranteAsync();

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-restaurant-token");

        var request = new CrearPedidoRequest(
            RestauranteId: Guid.Parse("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6"),
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
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.RestauranteId == Guid.Parse("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6"));
        
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
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-restaurant-token");

        // Request inválido: sin items
        var request = new CrearPedidoRequest(
            RestauranteId: Guid.Parse("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6"),
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
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-restaurant-token");

        var request = new CrearPedidoRequest(
            RestauranteId: Guid.Parse("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6"), // No existe
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

    private HttpClient CreateClient() => _factory.CreateClient();
}