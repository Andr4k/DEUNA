using System.Security.Claims;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.Services;
using Deuna.Shared.Domain;
using Deuna.Shared.Events;
using Deuna.Shared.Security;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NetTopologySuite.Geometries;

namespace Deuna.Orders.Service.Tests.Integration;

/// <summary>
/// Que la cancelación PUBLIQUE el evento.
///
/// Es la línea de la que depende todo el arreglo: sin PedidoCancelado, Delivery no se entera,
/// allá el pedido sigue contando como activo y el domiciliario queda ocupado para siempre.
/// El resto de los tests de Orders mira el estado en la base; este es el único que mira el bus.
///
/// Va a nivel de servicio y no por HTTP a propósito: el evento no se puede observar desde
/// afuera, y un mock del publicador lo fija sin depender de la infraestructura de mensajería.
/// </summary>
public class CancelarPedidoEventoTests : IAsyncLifetime
{
    private OrdersDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(PostgisTestContainer.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options);

        // Migraciones y no EnsureCreated: el esquema tiene objetos que viven en SQL crudo
        // (PostGIS) y EnsureCreated no los ve.
        await _db.Database.MigrateAsync();
        await TestData.ResetAsync(_db);
        await TestData.SeedRestauranteAsync(_db);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private static HttpContext ContextoDelRestaurante(Guid restauranteId)
    {
        var identidad = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, restauranteId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Restaurant)
        ], "Test");

        return new DefaultHttpContext { User = new ClaimsPrincipal(identidad) };
    }

    private PedidoService CrearServicio(Mock<IPublishEndpoint> publish) =>
        new(_db,
            Mock.Of<ITarifaService>(),
            Mock.Of<IGeoService>(),
            publish.Object,
            Options.Create(new OrdersOptions()),
            NullLogger<PedidoService>.Instance);

    private async Task<Pedido> SeedPedidoAsync(string estado)
    {
        var direccion = new DireccionEntrega
        {
            Calle = "Calle 45",
            Numero = "#23-10",
            Ciudad = "Bogotá",
            Departamento = "Cundinamarca",
            CodigoPostal = "110111",
            Ubicacion = new Point(-74.0817500, 4.6097100) { SRID = 4326 }
        };

        _db.DireccionesEntrega.Add(direccion);

        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            ClienteId = Guid.NewGuid(),
            RestauranteId = TestData.RestauranteId,
            Codigo = $"PED-EVT-{Random.Shared.Next(100000, 999999)}",
            Estado = estado,
            Subtotal = 25000m,
            CostoEnvio = 5000m,
            Total = 30000m,
            DireccionEntregaId = direccion.Id,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            FechaCreacion = DateTime.UtcNow
        };

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();
        return pedido;
    }

    [Fact]
    public async Task CancelarPublicaElEventoConElPedidoYElMotivo()
    {
        var pedido = await SeedPedidoAsync(EstadosPedido.Buscando);
        var publish = new Mock<IPublishEndpoint>();

        await CrearServicio(publish).CancelarPedidoAsync(
            pedido.Id, "Sin insumos", ContextoDelRestaurante(pedido.RestauranteId));

        publish.Verify(p => p.Publish(
            It.Is<PedidoCancelado>(e =>
                e.PedidoId == pedido.Id &&
                e.Codigo == pedido.Codigo &&
                e.Motivo == "Sin insumos"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnRechazoNoPublicaNada()
    {
        // La contracara, y la que más importa: si el pedido no se puede cancelar, no puede
        // salir un evento que diga que se canceló. Un publish colocado antes de validar
        // dejaría a Delivery cancelando un pedido que sigue vivo.
        var pedido = await SeedPedidoAsync(EstadosPedido.EnRuta);
        var publish = new Mock<IPublishEndpoint>();

        var cancelar = () => CrearServicio(publish).CancelarPedidoAsync(
            pedido.Id, "Llegué tarde", ContextoDelRestaurante(pedido.RestauranteId));

        await cancelar.Should().ThrowAsync<InvalidOperationException>();

        publish.Verify(
            p => p.Publish(It.IsAny<PedidoCancelado>(), It.IsAny<CancellationToken>()),
            Times.Never);

        (await _db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedido.Id))
            .Estado.Should().Be(EstadosPedido.EnRuta, "un rechazo no puede cambiar nada");
    }
}
