using Deuna.Orders.Service;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit;

namespace Deuna.Orders.Service.Tests.Features;

public class GeoServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public GeoServiceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CalcularDistanciaMetrosAsync_ReturnsCorrectDistance()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var geoService = scope.ServiceProvider.GetRequiredService<IGeoService>();

        // Bogotá (4.6097100, -74.0817500) a punto cercano (~1km)
        var lat1 = 4.6097100;
        var lon1 = -74.0817500;
        var lat2 = 4.6197100; // ~1.1 km al norte
        var lon2 = -74.0817500;

        // Act
        var distancia = await geoService.CalcularDistanciaMetrosAsync(lat1, lon1, lat2, lon2);

        // Assert
        distancia.Should().BeGreaterThan(1000); // ~1.1 km = 1100 metros
        distancia.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task EstaEnRadioCoberturaAsync_ReturnsTrue_WhenWithinRadius()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var geoService = scope.ServiceProvider.GetRequiredService<IGeoService>();

        // Mismo punto, radio 5km
        var latPedido = 4.6097100;
        var lonPedido = -74.0817500;
        var latRestaurante = 4.6097100;
        var lonRestaurante = -74.0817500;
        var radioKm = 5.0;

        // Act
        var enRadio = await geoService.EstaEnRadioCoberturaAsync(latPedido, lonPedido, latRestaurante, lonRestaurante, radioKm);

        // Assert
        enRadio.Should().BeTrue();
    }

    [Fact]
    public async Task EstaEnRadioCoberturaAsync_ReturnsTrue_WhenAtEdgeOfRadius()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var geoService = scope.ServiceProvider.GetRequiredService<IGeoService>();

        // Punto a ~1km de distancia, radio 5km
        var latPedido = 4.6197100;
        var lonPedido = -74.0817500;
        var latRestaurante = 4.6097100;
        var lonRestaurante = -74.0817500;
        var radioKm = 5.0;

        // Act
        var enRadio = await geoService.EstaEnRadioCoberturaAsync(latPedido, lonPedido, latRestaurante, lonRestaurante, radioKm);

        // Assert
        enRadio.Should().BeTrue();
    }

    [Fact]
    public async Task EstaEnRadioCoberturaAsync_ReturnsFalse_WhenOutsideRadius()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var geoService = scope.ServiceProvider.GetRequiredService<IGeoService>();

        // Bogotá a Medellín ~415 km, radio 5km
        var latPedido = 6.2442; // Medellín
        var lonPedido = -75.5812;
        var latRestaurante = 4.6097100; // Bogotá
        var lonRestaurante = -74.0817500;
        var radioKm = 5.0;

        // Act
        var enRadio = await geoService.EstaEnRadioCoberturaAsync(latPedido, lonPedido, latRestaurante, lonRestaurante, radioKm);

        // Assert
        enRadio.Should().BeFalse();
    }
}

public class TarifaServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TarifaServiceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task ResetAndSeedAsync(OrdersDbContext db)
    {
        // Limpiar antes de sembrar: los tests de esta clase comparten la base y el
        // restaurante tiene clave primaria fija, así que el segundo test chocaría
        // con el registro del primero.
        await TestData.ResetAsync(db);
        await TestData.SeedRestauranteAsync(db);
    }

    [Fact]
    public async Task CalcularTarifaAsync_ReturnsTarifaDistancia_WhenWithinRadius()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var tarifaService = scope.ServiceProvider.GetRequiredService<ITarifaService>();

        await ResetAndSeedAsync(db);

        var items = new List<ItemPedido>
        {
            new() { NombreProducto = "Hamburguesa", Cantidad = 2, PrecioUnitario = 25000m, Subtotal = 50000m }
        };

        // Act - punto a ~1.67 km al norte del restaurante (dentro del radio de 10 km)
        var tarifa = await tarifaService.CalcularTarifaAsync(
            TestData.RestauranteId,
            4.6247100, -74.0817500,
            50000m,
            items);

        // Assert
        tarifa.Should().NotBeNull();
        tarifa.TipoTarifa.Should().Be("Distancia");
        tarifa.CostoBase.Should().Be(3000m);
        tarifa.CostoPorKm.Should().Be(1500m);
        tarifa.DistanciaKm.Should().BeGreaterThan(0);
        tarifa.TotalCalculado.Should().BeGreaterThan(tarifa.CostoBase);
        tarifa.DetalleCalculo.Should().Contain("Distancia");
    }

    [Fact]
    public async Task CalcularTarifaAsync_ThrowsException_WhenOutsideRadius()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var tarifaService = scope.ServiceProvider.GetRequiredService<ITarifaService>();

        await ResetAndSeedAsync(db);

        var items = new List<ItemPedido>
        {
            new() { NombreProducto = "Hamburguesa", Cantidad = 1, PrecioUnitario = 25000m, Subtotal = 25000m }
        };

        // Act & Assert - Medellín está fuera del radio de 10km de Bogotá
        await tarifaService.Invoking(s => s.CalcularTarifaAsync(
            TestData.RestauranteId,
            6.2442, -75.5812, // Medellín
            25000m,
            items))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fuera de cobertura*");
    }

    [Fact]
    public async Task CalcularTarifaAsync_ThrowsException_WhenRestaurantNotFound()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tarifaService = scope.ServiceProvider.GetRequiredService<ITarifaService>();

        var items = new List<ItemPedido>
        {
            new() { NombreProducto = "Hamburguesa", Cantidad = 1, PrecioUnitario = 25000m, Subtotal = 25000m }
        };

        // Act & Assert
        await tarifaService.Invoking(s => s.CalcularTarifaAsync(
            Guid.NewGuid(), // Restaurante inexistente
            4.6097100, -74.0817500,
            25000m,
            items))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no encontrado*");
    }
}