using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Delivery.Service.DTOs;
using Deuna.Delivery.Service.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Delivery.Service.Tests.Tracking;

[Collection(RedisCollection.Name)]
public class TrackingEndpointsTests : IAsyncLifetime
{
    private const string UpdateLocationUrl = "/api/v1/delivery/update-location";

    private readonly RedisContainerFixture _fixture;
    private TrackingWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public TrackingEndpointsTests(RedisContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _factory = new TrackingWebApplicationFactory(_fixture.ConnectionString);
        _client = _factory.CreateClient();

        // La BD InMemory se comparte por nombre dentro del proceso: se limpia entre tests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        db.PedidosDisponibles.RemoveRange(db.PedidosDisponibles);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<PedidoDisponible> SembrarPedidoAsync(Guid? repartidorId = null, double? latitud = 4.6097, double? longitud = -74.0817)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-TEST-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 55000m,
            Estado = PedidoDisponible.EstadoBuscando,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            TokenQrEntrega = Guid.NewGuid().ToString("N"),
            Latitud = latitud,
            Longitud = longitud,
            RepartidorId = repartidorId
        };

        db.PedidosDisponibles.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private async Task PostUbicacionAsync(Guid riderId, double latitud, double longitud, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, UpdateLocationUrl)
        {
            Content = JsonContent.Create(new ActualizarUbicacionRequest(riderId, latitud, longitud, DateTime.UtcNow))
        };

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateLocation_SinToken_Devuelve401()
    {
        var response = await _client.PostAsJsonAsync(
            UpdateLocationUrl,
            new ActualizarUbicacionRequest(Guid.NewGuid(), 4.6097, -74.0817, DateTime.UtcNow));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateLocation_ConRolRestaurante_Devuelve403()
    {
        var riderId = Guid.NewGuid();
        var token = TrackingWebApplicationFactory.CrearToken(riderId, "RESTAURANT");

        var request = new HttpRequestMessage(HttpMethod.Post, UpdateLocationUrl)
        {
            Content = JsonContent.Create(new ActualizarUbicacionRequest(riderId, 4.6097, -74.0817, DateTime.UtcNow))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateLocation_ConRiderDistintoAlDelToken_Devuelve403()
    {
        var token = TrackingWebApplicationFactory.CrearToken(Guid.NewGuid());
        var otroRider = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, UpdateLocationUrl)
        {
            Content = JsonContent.Create(new ActualizarUbicacionRequest(otroRider, 4.6097, -74.0817, DateTime.UtcNow))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // Un repartidor no puede suplantar la ubicación de otro
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateLocation_ConTokenValido_Devuelve204()
    {
        var riderId = Guid.NewGuid();
        var token = TrackingWebApplicationFactory.CrearToken(riderId);

        await PostUbicacionAsync(riderId, 4.6097, -74.0817, token);
    }

    [Fact]
    public async Task Tracking_DevuelveLaUbicacionDelRepartidorAsignado()
    {
        var riderId = Guid.NewGuid();
        var pedido = await SembrarPedidoAsync(repartidorId: riderId);
        await PostUbicacionAsync(riderId, 4.6580, -74.0560, TrackingWebApplicationFactory.CrearToken(riderId));

        var response = await _client.SendAsync(ConToken(Guid.NewGuid(), $"/api/v1/delivery/tracking/{pedido.PedidoId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ubicacion = await response.Content.ReadFromJsonAsync<UbicacionRepartidorResponse>();
        ubicacion!.OrderId.Should().Be(pedido.PedidoId);
        ubicacion.RiderId.Should().Be(riderId);
        ubicacion.Latitude.Should().BeApproximately(4.6580, 0.0001);
        ubicacion.Longitude.Should().BeApproximately(-74.0560, 0.0001);
    }

    [Fact]
    public async Task Tracking_SinRepartidorAsignado_DevuelveElMasCercanoAlDestino()
    {
        var pedido = await SembrarPedidoAsync(latitud: 4.6097, longitud: -74.0817);
        var cercano = Guid.NewGuid();
        var lejano = Guid.NewGuid();

        await PostUbicacionAsync(cercano, 4.6300, -74.0817, TrackingWebApplicationFactory.CrearToken(cercano));
        await PostUbicacionAsync(lejano, 4.7100, -74.0817, TrackingWebApplicationFactory.CrearToken(lejano));

        var response = await _client.SendAsync(ConToken(Guid.NewGuid(), $"/api/v1/delivery/tracking/{pedido.PedidoId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ubicacion = await response.Content.ReadFromJsonAsync<UbicacionRepartidorResponse>();
        ubicacion!.RiderId.Should().Be(cercano);
        ubicacion.DistanciaMetros.Should().NotBeNull();
    }

    [Fact]
    public async Task Tracking_ConPedidoInexistente_Devuelve404()
    {
        var response = await _client.SendAsync(ConToken(Guid.NewGuid(), $"/api/v1/delivery/tracking/{Guid.NewGuid()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tracking_SinTelemetria_Devuelve404()
    {
        var riderId = Guid.NewGuid();
        var pedido = await SembrarPedidoAsync(repartidorId: riderId);

        var response = await _client.SendAsync(ConToken(Guid.NewGuid(), $"/api/v1/delivery/tracking/{pedido.PedidoId}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tracking_SinToken_Devuelve401()
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.GetAsync($"/api/v1/delivery/tracking/{pedido.PedidoId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpRequestMessage ConToken(Guid riderId, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TrackingWebApplicationFactory.CrearToken(riderId));
        return request;
    }
}
