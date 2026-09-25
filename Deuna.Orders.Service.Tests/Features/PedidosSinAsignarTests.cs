using System.Net;
using System.Net.Http.Json;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit;

namespace Deuna.Orders.Service.Tests.Features;

/// <summary>
/// Contrato HTTP del listado de administración, tal como lo consume el portal.
/// Se deserializa en tipos locales a propósito: estos tests prueban el contrato por
/// HTTP (ruta, query, forma del JSON), no las clases internas del servicio.
/// </summary>
internal sealed record ListaSinAsignarDto(
    List<ItemSinAsignarDto> Items,
    int Total,
    int Pagina,
    int Tamano);

internal sealed record ItemSinAsignarDto(
    Guid PedidoId,
    string Codigo,
    string Restaurante,
    string RecogerEn,
    string EntregarEn,
    DateTime GeneradoEn,
    int MinutosEsperando,
    string Prioridad,
    decimal ValorDomicilio,
    string Zona,
    string Estado);

/// <summary>
/// Listado de pedidos sin asignar para el panel del administrador.
///
/// Los pedidos se siembran directo contra el <see cref="OrdersDbContext"/>: crear un
/// pedido "Asignado" por la API no es posible (el alta siempre nace en Buscando), y el
/// valor del domicilio tiene que quedar bajo control del test para probar los rangos
/// de prioridad.
/// </summary>
public class PedidosSinAsignarTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Ruta = "/api/v1/admin/orders/sin-asignar";

    private readonly TestWebApplicationFactory _factory;

    public PedidosSinAsignarTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.CreateAdminToken(Guid.NewGuid()));
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await TestData.ResetAsync(db);
        await TestData.SeedRestauranteAsync(db);
    }

    /// <summary>
    /// Siembra un restaurante en otra ciudad. El listado filtra por la ciudad del
    /// restaurante, así que hace falta más de una para probar el filtro.
    /// </summary>
    private async Task<Guid> SeedRestauranteEnCiudadAsync(string ciudad, string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var id = Guid.NewGuid();
        db.RestaurantesReplicados.Add(new RestauranteReplicado
        {
            Id = id,
            NombreComercial = nombre,
            RazonSocial = $"{nombre} S.A.S.",
            Nit = $"900{Guid.NewGuid().ToString("N")[..7]}-1",
            DireccionSede = "Carrera 70 #45-12",
            Ciudad = ciudad,
            Latitud = 6.2442000m,
            Longitud = -75.5812000m,
            Ubicacion = new Point(-75.5812000, 6.2442000) { SRID = 4326 },
            RadioCoberturaKm = 10.0m,
            AceptaPedidos = true,
            Activo = true
        });

        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Siembra un pedido con su dirección y su tarifa aplicada. El valor del domicilio
    /// se fija en <paramref name="valorDomicilio"/> porque es la entrada de la regla de
    /// prioridad y el criterio de orden.
    /// </summary>
    private async Task<Guid> SeedPedidoAsync(
        Guid restauranteId,
        decimal valorDomicilio,
        string estado = EstadosPedido.Buscando,
        DateTime? fechaCreacion = null,
        DateTime? fechaAsignacion = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var direccion = new DireccionEntrega
        {
            Calle = "Calle 45",
            Numero = "#23-10",
            Ciudad = "Bogotá",
            Ubicacion = new Point(-74.0817500, 4.6097100) { SRID = 4326 }
        };
        db.DireccionesEntrega.Add(direccion);
        await db.SaveChangesAsync();

        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = restauranteId,
            Codigo = "PED-" + Guid.NewGuid().ToString("N")[..16],
            Estado = estado,
            Subtotal = 50000m,
            CostoEnvio = valorDomicilio,
            Total = 50000m + valorDomicilio,
            DireccionEntregaId = direccion.Id,
            TokenQrLocal = Guid.NewGuid().ToString("N")[..32],
            TokenQrEntrega = Guid.NewGuid().ToString("N")[..32],
            FechaCreacion = fechaCreacion ?? DateTime.UtcNow
        };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        db.TarifasAplicadas.Add(new TarifaAplicada
        {
            PedidoId = pedido.Id,
            TipoTarifa = "Distancia",
            CostoBase = 3000m,
            TotalCalculado = valorDomicilio
        });

        if (fechaAsignacion.HasValue)
        {
            db.AsignacionesReplicadas.Add(new AsignacionReplicada
            {
                PedidoId = pedido.Id,
                RepartidorId = Guid.NewGuid(),
                FechaAsignacion = fechaAsignacion.Value,
                EstadoAsignacion = AsignacionReplicada.EstadoAsignado
            });
        }

        await db.SaveChangesAsync();
        return pedido.Id;
    }

    private async Task<ListaSinAsignarDto> GetListaAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"{Ruta}{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await response.Content.ReadFromJsonAsync<ListaSinAsignarDto>();
        lista.Should().NotBeNull();
        return lista!;
    }

    [Fact]
    public async Task ListaSinAsignar_DevuelveSoloBuscando()
    {
        // Arrange
        await ResetDatabaseAsync();
        var sinAsignar = await SeedPedidoAsync(TestData.RestauranteId, 5000m, EstadosPedido.Buscando);
        await SeedPedidoAsync(
            TestData.RestauranteId, 5000m, EstadosPedido.Asignado,
            fechaAsignacion: DateTime.UtcNow.AddMinutes(-1));
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client);

        // Assert
        lista.Total.Should().Be(1);
        lista.Items.Should().HaveCount(1);
        lista.Items[0].PedidoId.Should().Be(sinAsignar);
        // El estado viaja en el ítem: cuando se encienda el umbral de "no responde", la
        // lista va a mezclar Buscando con Asignado y la pantalla necesita distinguirlos.
        lista.Items[0].Estado.Should().Be(EstadosPedido.Buscando);
    }

    [Fact]
    public async Task ListaSinAsignar_OrdenaPorValorDescendente()
    {
        // Arrange: valores en orden deliberadamente mezclado
        await ResetDatabaseAsync();
        await SeedPedidoAsync(TestData.RestauranteId, 3000m);
        await SeedPedidoAsync(TestData.RestauranteId, 9000m);
        await SeedPedidoAsync(TestData.RestauranteId, 6000m);
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client);

        // Assert: primero el de valor más alto
        lista.Items.Select(i => i.ValorDomicilio).Should().Equal(9000m, 6000m, 3000m);
    }

    [Fact]
    public async Task ListaSinAsignar_AplicaPrioridadPorRangoDePrecio()
    {
        // Arrange: los cortes salen de configuración, no de constantes de código.
        // Se leen para sembrar valores relativos a ellos y que el test los exija.
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var altaDesde = config.GetValue<decimal>("Orders:PrioridadAltaDesde");
        var mediaDesde = config.GetValue<decimal>("Orders:PrioridadMediaDesde");

        altaDesde.Should().BeGreaterThan(0, "los cortes de prioridad tienen que venir de appsettings.json");
        mediaDesde.Should().BeGreaterThan(0);
        altaDesde.Should().BeGreaterThan(mediaDesde);

        await ResetDatabaseAsync();
        var caro = await SeedPedidoAsync(TestData.RestauranteId, altaDesde + 1000m);
        var medio = await SeedPedidoAsync(TestData.RestauranteId, (altaDesde + mediaDesde) / 2m);
        var barato = await SeedPedidoAsync(TestData.RestauranteId, mediaDesde - 1000m);
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client);

        // Assert
        lista.Items.Single(i => i.PedidoId == caro).Prioridad.Should().Be("Alta");
        lista.Items.Single(i => i.PedidoId == medio).Prioridad.Should().Be("Media");
        lista.Items.Single(i => i.PedidoId == barato).Prioridad.Should().Be("Baja");
    }

    [Fact]
    public async Task ListaSinAsignar_FiltraPorZona()
    {
        // Arrange
        await ResetDatabaseAsync();
        var bogota = await SeedPedidoAsync(TestData.RestauranteId, 5000m);
        var medellinId = await SeedRestauranteEnCiudadAsync("Medellín", "Sabor Paisa");
        var medellin = await SeedPedidoAsync(medellinId, 5000m);
        using var client = CreateAdminClient();

        // Act: la ciudad llega en minúsculas y con espacios; el filtro es case-insensitive
        var lista = await GetListaAsync(client, "?zona=%20medell%C3%ADn%20");

        // Assert
        lista.Total.Should().Be(1);
        lista.Items.Should().HaveCount(1);
        lista.Items[0].PedidoId.Should().Be(medellin);
        lista.Items[0].Zona.Should().Be("Medellín");
        lista.Items.Should().NotContain(i => i.PedidoId == bogota);
    }

    [Fact]
    public async Task ListaSinAsignar_PaginaYDevuelveTotal()
    {
        // Arrange: 12 pedidos con valores 1000..12000
        await ResetDatabaseAsync();
        for (var valor = 1000m; valor <= 12000m; valor += 1000m)
        {
            await SeedPedidoAsync(TestData.RestauranteId, valor);
        }
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client, "?pagina=2&tamano=5");

        // Assert: el total es el de la lista completa, no el de la página
        lista.Total.Should().Be(12);
        lista.Pagina.Should().Be(2);
        lista.Tamano.Should().Be(5);
        lista.Items.Should().HaveCount(5);
        // Orden valor descendente: página 2 = posiciones 6..10
        lista.Items.Select(i => i.ValorDomicilio).Should().Equal(7000m, 6000m, 5000m, 4000m, 3000m);
    }

    [Fact]
    public async Task ListaSinAsignar_FiltraPorEsperaMinima()
    {
        // Arrange: uno esperando hace 30 minutos y otro recién creado. El filtro es por
        // TIEMPO, no por valor: el que entra es el barato y viejo, no el caro y nuevo.
        // Así el test distingue "filtrar por espera" de "devolver todo".
        await ResetDatabaseAsync();
        var viejo = await SeedPedidoAsync(
            TestData.RestauranteId, 3000m, fechaCreacion: DateTime.UtcNow.AddMinutes(-30));
        var nuevo = await SeedPedidoAsync(TestData.RestauranteId, 9000m);
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client, "?esperaMin=15");

        // Assert
        lista.Total.Should().Be(1);
        lista.Items.Should().ContainSingle().Which.PedidoId.Should().Be(viejo);
        lista.Items.Should().NotContain(i => i.PedidoId == nuevo);
    }

    [Fact]
    public async Task ListaSinAsignar_SinUmbral_NoTraeSinRespuesta()
    {
        // Arrange: el umbral de "no responde" viene apagado por defecto (centinela 0).
        // Aun pidiendo incluirSinRespuesta=true, un Asignado no puede aparecer.
        await ResetDatabaseAsync();
        var buscando = await SeedPedidoAsync(TestData.RestauranteId, 5000m, EstadosPedido.Buscando);
        await SeedPedidoAsync(
            TestData.RestauranteId, 5000m, EstadosPedido.Asignado,
            fechaAsignacion: DateTime.UtcNow.AddHours(-2));
        using var client = CreateAdminClient();

        // Act
        var lista = await GetListaAsync(client, "?incluirSinRespuesta=true");

        // Assert
        lista.Total.Should().Be(1);
        lista.Items.Should().ContainSingle().Which.PedidoId.Should().Be(buscando);
    }

    [Fact]
    public async Task ListaSinAsignar_SinRolAdmin_Devuelve403()
    {
        // Arrange: un token de restaurante no entra al panel de administración
        await ResetDatabaseAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.CreateRestaurantToken(TestData.RestauranteId));

        // Act
        var response = await client.GetAsync(Ruta);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// Contenedor con el umbral de "no responde" encendido. El valor por defecto va apagado
/// (centinela 0), así que la única forma de probar que el filtro funciona cuando se
/// configura es levantar la API con la sección Orders sobrescrita.
/// </summary>
public sealed class UmbralSinRespuestaFactory : TestWebApplicationFactory
{
    private const int UmbralMinutos = 30;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Se agrega al final: la última fuente gana, así sobrescribe lo que dejó la base.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orders:MinutosSinRespuesta"] = UmbralMinutos.ToString()
            }));
    }
}

public class PedidosSinAsignarConUmbralTests : IClassFixture<UmbralSinRespuestaFactory>
{
    private const string Ruta = "/api/v1/admin/orders/sin-asignar";

    private readonly UmbralSinRespuestaFactory _factory;

    public PedidosSinAsignarConUmbralTests(UmbralSinRespuestaFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.CreateAdminToken(Guid.NewGuid()));
        return client;
    }

    [Fact]
    public async Task ListaSinAsignar_ConUmbral_TraeLosVencidos()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await TestData.ResetAsync(db);
            await TestData.SeedRestauranteAsync(db);
        }

        var vencido = await SeedPedidoAsync(
            TestData.RestauranteId, 5000m, EstadosPedido.Asignado,
            fechaAsignacion: DateTime.UtcNow.AddMinutes(-45));
        await SeedPedidoAsync(
            TestData.RestauranteId, 5000m, EstadosPedido.Asignado,
            fechaAsignacion: DateTime.UtcNow.AddMinutes(-5));
        var buscando = await SeedPedidoAsync(TestData.RestauranteId, 5000m, EstadosPedido.Buscando);
        using var client = CreateAdminClient();

        // Act
        var response = await client.GetAsync($"{Ruta}?incluirSinRespuesta=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await response.Content.ReadFromJsonAsync<ListaSinAsignarDto>();

        // Assert: aparece el que superó el umbral, no el recién asignado
        lista.Should().NotBeNull();
        lista!.Total.Should().Be(2);
        lista.Items.Select(i => i.PedidoId).Should().BeEquivalentTo(new[] { vencido, buscando });
    }

    private async Task<Guid> SeedPedidoAsync(
        Guid restauranteId,
        decimal valorDomicilio,
        string estado,
        DateTime? fechaAsignacion = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var direccion = new DireccionEntrega
        {
            Calle = "Calle 45",
            Numero = "#23-10",
            Ciudad = "Bogotá",
            Ubicacion = new Point(-74.0817500, 4.6097100) { SRID = 4326 }
        };
        db.DireccionesEntrega.Add(direccion);
        await db.SaveChangesAsync();

        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = restauranteId,
            Codigo = "PED-" + Guid.NewGuid().ToString("N")[..16],
            Estado = estado,
            Subtotal = 50000m,
            CostoEnvio = valorDomicilio,
            Total = 50000m + valorDomicilio,
            DireccionEntregaId = direccion.Id,
            TokenQrLocal = Guid.NewGuid().ToString("N")[..32],
            TokenQrEntrega = Guid.NewGuid().ToString("N")[..32],
            FechaCreacion = DateTime.UtcNow.AddMinutes(-60)
        };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        db.TarifasAplicadas.Add(new TarifaAplicada
        {
            PedidoId = pedido.Id,
            TipoTarifa = "Distancia",
            CostoBase = 3000m,
            TotalCalculado = valorDomicilio
        });

        if (fechaAsignacion.HasValue)
        {
            db.AsignacionesReplicadas.Add(new AsignacionReplicada
            {
                PedidoId = pedido.Id,
                RepartidorId = Guid.NewGuid(),
                FechaAsignacion = fechaAsignacion.Value,
                EstadoAsignacion = AsignacionReplicada.EstadoAsignado
            });
        }

        await db.SaveChangesAsync();
        return pedido.Id;
    }
}
