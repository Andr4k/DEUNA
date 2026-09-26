using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace Deuna.Orders.Service.Tests.Features;

/// <summary>
/// Cancelación de un pedido desde el restaurante.
///
/// Orders es el dueño del pedido y el contrato de PedidoCancelado lo dice: "Publicado por
/// Orders cuando se cancela un pedido". Hasta ahora nadie lo publicaba, así que un pedido
/// cancelado seguía existiendo como activo en Delivery y ocupaba a un domiciliario.
///
/// La regla es la que ya se había definido: se cancela mientras el pedido no esté
/// confirmado en el local. Después, la comida ya está en juego y el camino es otro.
/// </summary>
public class CancelarPedidoTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CancelarPedidoTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await TestData.ResetAsync(db);
        await TestData.SeedRestauranteAsync(db);
    }

    private HttpClient CreateRestaurantClient(Guid? restauranteId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateRestaurantToken(restauranteId ?? TestData.RestauranteId));
        return client;
    }

    private async Task<Pedido> SeedPedidoAsync(string estado, Guid? restauranteId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // La dirección es una fila real, no un id suelto: el pedido la referencia con FK y
        // sembrar un Guid.NewGuid() hace fallar el insert.
        var direccion = new DireccionEntrega
        {
            Calle = "Calle 45",
            Numero = "#23-10",
            Ciudad = "Bogotá",
            Departamento = "Cundinamarca",
            CodigoPostal = "110111",
            Ubicacion = new Point(-74.0817500, 4.6097100) { SRID = 4326 }
        };

        db.DireccionesEntrega.Add(direccion);

        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            ClienteId = Guid.NewGuid(),
            RestauranteId = restauranteId ?? TestData.RestauranteId,
            Codigo = $"PED-CANC-{Random.Shared.Next(100000, 999999)}",
            Estado = estado,
            Subtotal = 25000m,
            CostoEnvio = 5000m,
            Total = 30000m,
            DireccionEntregaId = direccion.Id,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            FechaCreacion = DateTime.UtcNow
        };

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private async Task<Pedido> LeerPedidoAsync(Guid pedidoId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
    }

    private static string Ruta(Guid pedidoId) => $"/api/v1/orders/{pedidoId}/cancelar";

    // ------------------------------------------------------------------ cancela

    [Fact]
    public async Task CancelaUnPedidoEnBuscando()
    {
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(EstadosPedido.Buscando);
        using var client = CreateRestaurantClient();

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "Sin insumos" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var actualizado = await LeerPedidoAsync(pedido.Id);
        actualizado.Estado.Should().Be(EstadosPedido.Cancelado);
        actualizado.FechaCancelacion.Should().NotBeNull("la fecha es lo que deja el rastro de cuándo se canceló");
    }

    [Fact]
    public async Task CancelaUnPedidoYaAsignado()
    {
        // Todavía se puede: el domiciliario va en camino pero no llegó al local. Cancelar
        // acá lo libera, y por eso Delivery tiene que enterarse.
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(EstadosPedido.Asignado);
        using var client = CreateRestaurantClient();

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "El cliente se arrepintió" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerPedidoAsync(pedido.Id)).Estado.Should().Be(EstadosPedido.Cancelado);
    }

    // ------------------------------------------------------------------ rechaza

    [Theory]
    [InlineData(EstadosPedido.ConfirmadoEnLocal)]
    [InlineData(EstadosPedido.EnRuta)]
    [InlineData(EstadosPedido.Entregado)]
    [InlineData(EstadosPedido.Cancelado)]
    public async Task NoCancelaUnPedidoQueYaPasoElPuntoDeNoRetorno(string estado)
    {
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(estado);
        using var client = CreateRestaurantClient();

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "Tarde" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerPedidoAsync(pedido.Id)).Estado.Should().Be(estado, "un rechazo no puede cambiar nada");
    }

    [Fact]
    public async Task UnPedidoInexistenteDevuelve404()
    {
        await ResetDatabaseAsync();
        using var client = CreateRestaurantClient();

        var respuesta = await client.PostAsJsonAsync(Ruta(Guid.NewGuid()), new { motivo = "No existe" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OtroRestauranteNoPuedeCancelarlo()
    {
        // El pedido es de un restaurante y lo cancela otro: 403, no 404. El pedido existe,
        // lo que falta es el permiso.
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(EstadosPedido.Buscando);
        using var client = CreateRestaurantClient(Guid.NewGuid());

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "No es mío" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeerPedidoAsync(pedido.Id)).Estado.Should().Be(EstadosPedido.Buscando);
    }

    // -------------------------------------------------------------------- puerta

    [Fact]
    public async Task SinTokenDevuelve401()
    {
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(EstadosPedido.Buscando);
        using var client = _factory.CreateClient();

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "Sin credenciales" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnClienteNoPuedeCancelarUnPedido()
    {
        // El grupo exige rol RESTAURANT: cancelar es del restaurante, que es quien recibe y
        // prepara el pedido. Un cliente no cancela por esta vía.
        await ResetDatabaseAsync();
        var pedido = await SeedPedidoAsync(EstadosPedido.Buscando);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateAdminToken(Guid.NewGuid()));

        var respuesta = await client.PostAsJsonAsync(Ruta(pedido.Id), new { motivo = "No debería" });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
