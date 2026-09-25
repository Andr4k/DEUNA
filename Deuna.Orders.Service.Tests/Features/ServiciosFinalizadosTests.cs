using System.Net;
using System.Net.Http.Json;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit;

namespace Deuna.Orders.Service.Tests.Features;

/// <summary>
/// Contrato HTTP del historial de servicios finalizados, tal como lo consume el portal.
/// Se deserializa en tipos locales a propósito: estos tests prueban el contrato por HTTP
/// (ruta, query, forma del JSON), no las clases internas del servicio.
/// </summary>
internal sealed record RespuestaServiciosFinalizadosDto(
    PaginaServiciosFinalizadosDto Pagina,
    ResumenServiciosFinalizadosDto Resumen);

internal sealed record PaginaServiciosFinalizadosDto(
    List<ServicioFinalizadoDto> Items,
    int Total,
    int Pagina,
    int Tamano);

internal sealed record ServicioFinalizadoDto(
    Guid PedidoId,
    string Codigo,
    DateTime CerradoEn,
    RestauranteDelServicioDto Restaurante,
    DomiciliarioDelServicioDto? Domiciliario,
    OrigenDelServicioDto Origen,
    DestinoDelServicioDto Destino,
    TiemposDelServicioDto Tiempos,
    ValorDelServicioDto Valor,
    CalificacionesDelServicioDto Calificaciones,
    IncidenciaDelServicioDto Incidencia,
    string Estado);

internal sealed record RestauranteDelServicioDto(Guid Id, string Nombre, string Ciudad);

internal sealed record DomiciliarioDelServicioDto(Guid Id, string Nombre, double? Calificacion);

internal sealed record OrigenDelServicioDto(string Direccion);

internal sealed record DestinoDelServicioDto(string Direccion, string Zona);

internal sealed record TiemposDelServicioDto(
    DateTime? AsignadoEn,
    DateTime? RecogidoEn,
    DateTime EntregadoEn,
    int? MinutosTotales);

internal sealed record ValorDelServicioDto(decimal Domicilio, decimal Total, string? Paga);

internal sealed record CalificacionesDelServicioDto(double? Domiciliario, double? Restaurante);

internal sealed record IncidenciaDelServicioDto(bool Hay, string? Motivo);

internal sealed record ResumenServiciosFinalizadosDto(
    int CompletadosHoy,
    decimal ValorDomiciliosHoy,
    double? CalificacionPromedio,
    int CalificacionesContadas,
    int? ConIncidencias,
    int? TiempoPromedioMin,
    AyerDelResumenDto Ayer);

internal sealed record AyerDelResumenDto(
    int Completados,
    decimal ValorDomicilios,
    int? TiempoPromedioMin);

/// <summary>
/// Historial de servicios finalizados para el panel del administrador.
///
/// Los servicios se siembran directo contra el <see cref="OrdersDbContext"/>: un pedido
/// "Entregado" con asignación, domiciliario y calificación no se puede armar por la API
/// (el alta nace en Buscando y el ciclo lo cierran los eventos de Delivery y Feedback).
///
/// El test que más importa acá es el primero: los KPIs y las filas tienen que salir del
/// MISMO conjunto. Un contador que dice 5 mientras la lista muestra otra cosa es el bug
/// que ya tuvimos en el panel principal.
/// </summary>
public class ServiciosFinalizadosTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Ruta = "/api/v1/admin/orders/servicios-finalizados";

    private readonly TestWebApplicationFactory _factory;

    public ServiciosFinalizadosTests(TestWebApplicationFactory factory)
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

    private async Task<RespuestaServiciosFinalizadosDto> GetAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"{Ruta}{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var respuesta = await response.Content.ReadFromJsonAsync<RespuestaServiciosFinalizadosDto>();
        respuesta.Should().NotBeNull();
        return respuesta!;
    }

    /// <summary>
    /// Siembra un restaurante en otra ciudad. El filtro de zona usa la ciudad de la
    /// dirección de entrega, así que hace falta más de un restaurante para probarlo.
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
    /// Siembra un servicio ya cerrado: el pedido entregado, su dirección, su tarifa y —si
    /// se pasa <paramref name="repartidorId"/>— el intento de entrega con su domiciliario,
    /// sus hitos y su encuesta. El valor del domicilio se fija porque es la entrada del
    /// KPI de valor y de la columna de valor.
    /// </summary>
    private async Task<Guid> SeedServicioAsync(
        Guid restauranteId,
        decimal valorDomicilio,
        DateTime fechaEntrega,
        string ciudad = "Bogotá",
        Guid? repartidorId = null,
        string? nombreRepartidor = null,
        DateTime? fechaAsignacion = null,
        DateTime? fechaLlegadaLocal = null,
        int? calificacionDomiciliario = null,
        int? calificacionRestaurante = null,
        string? nombreCliente = null,
        string? codigo = null,
        string calle = "Calle 45")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var direccion = new DireccionEntrega
        {
            Calle = calle,
            Numero = "#23-10",
            Ciudad = ciudad,
            Ubicacion = new Point(-74.0817500, 4.6097100) { SRID = 4326 }
        };
        db.DireccionesEntrega.Add(direccion);
        await db.SaveChangesAsync();

        var pedido = new Pedido
        {
            ClienteId = Guid.NewGuid(),
            RestauranteId = restauranteId,
            Codigo = codigo ?? "PED-" + Guid.NewGuid().ToString("N")[..16],
            Estado = EstadosPedido.Entregado,
            Subtotal = 50000m,
            CostoEnvio = valorDomicilio,
            Total = 50000m + valorDomicilio,
            DireccionEntregaId = direccion.Id,
            TokenQrLocal = Guid.NewGuid().ToString("N")[..32],
            TokenQrEntrega = Guid.NewGuid().ToString("N")[..32],
            FechaCreacion = fechaEntrega.AddHours(-2),
            FechaEntrega = fechaEntrega
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

        if (nombreCliente is not null)
        {
            db.ContactosPedido.Add(new ContactoPedido
            {
                PedidoId = pedido.Id,
                Tipo = "Cliente",
                Nombre = nombreCliente,
                Telefono = "+573001112233"
            });
        }

        if (repartidorId is { } repartidor)
        {
            if (!await db.RepartidoresReplicados.AnyAsync(r => r.Id == repartidor))
            {
                db.RepartidoresReplicados.Add(new RepartidorReplicado
                {
                    Id = repartidor,
                    NombreCompleto = nombreRepartidor ?? $"Domiciliario {repartidor.ToString("N")[..6]}",
                    Activo = true
                });
            }

            db.AsignacionesReplicadas.Add(new AsignacionReplicada
            {
                PedidoId = pedido.Id,
                RepartidorId = repartidor,
                FechaAsignacion = fechaAsignacion ?? fechaEntrega.AddMinutes(-30),
                FechaLlegadaLocal = fechaLlegadaLocal,
                FechaEntrega = fechaEntrega,
                EstadoAsignacion = AsignacionReplicada.EstadoCompletado
            });
        }

        if (calificacionDomiciliario is { } notaDomiciliario && calificacionRestaurante is { } notaRestaurante)
        {
            db.CalificacionesReplicadas.Add(new CalificacionReplicada
            {
                PedidoId = pedido.Id,
                RepartidorId = repartidorId,
                CalificacionDomiciliario = notaDomiciliario,
                CalificacionRestaurante = notaRestaurante,
                FechaCalificacion = fechaEntrega.AddMinutes(15)
            });
        }

        await db.SaveChangesAsync();
        return pedido.Id;
    }

    [Fact]
    public async Task ServiciosFinalizados_LosKpisYLasFilasSalenDelMismoConjunto()
    {
        // Arrange: dos restaurantes en ciudades distintas y cinco servicios entregados hoy.
        // El filtro tiene que recortar las filas Y los KPIs: si el contador sale de otra
        // consulta, acá se ve.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;

        await SeedServicioAsync(TestData.RestauranteId, 3000m, hoy.AddHours(10), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 7000m, hoy.AddHours(12), "Bogotá");

        var medellin = await SeedRestauranteEnCiudadAsync("Medellín", "Sabor Paisa");
        await SeedServicioAsync(medellin, 4000m, hoy.AddHours(13), "Medellín");
        await SeedServicioAsync(medellin, 6000m, hoy.AddHours(14), "Medellín");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client, "?zona=" + Uri.EscapeDataString("bogotá"));

        // Assert: el conteo de la cabecera es el del conjunto filtrado, no el de la tabla
        respuesta.Pagina.Total.Should().Be(3);
        respuesta.Pagina.Items.Should().HaveCount(3);
        respuesta.Resumen.CompletadosHoy.Should().Be(respuesta.Pagina.Total,
            "los KPIs y las filas tienen que salir del mismo conjunto");
        respuesta.Resumen.CompletadosHoy.Should().Be(3);
        respuesta.Resumen.ValorDomiciliosHoy.Should().Be(15000m,
            "el valor de los domicilios es el de las mismas tres filas (3000 + 5000 + 7000)");
    }

    [Fact]
    public async Task ServiciosFinalizados_LosKpisCuentanElConjuntoYNoLaPagina()
    {
        // Arrange: cinco servicios, de a dos por página.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        for (var i = 0; i < 5; i++)
        {
            await SeedServicioAsync(TestData.RestauranteId, 1000m * (i + 1), hoy.AddHours(10 + i), "Bogotá");
        }

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client, "?pagina=2&tamano=2");

        // Assert: el bug del panel principal fue un contador que decía un número y una lista
        // que mostraba otro. El KPI es del conjunto, la página es de la página.
        respuesta.Pagina.Total.Should().Be(5);
        respuesta.Pagina.Items.Should().HaveCount(2);
        respuesta.Resumen.CompletadosHoy.Should().Be(5, "la cabecera cuenta el conjunto, no la página");
        respuesta.Resumen.CompletadosHoy.Should().Be(respuesta.Pagina.Total);
        respuesta.Resumen.ValorDomiciliosHoy.Should().Be(15000m, "el valor también es el de los cinco");
    }

    [Fact]
    public async Task ServiciosFinalizados_DevuelveLaFilaCompleta()
    {
        // Arrange: un servicio con domiciliario, sus hitos y su encuesta.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var repartidor = Guid.NewGuid();
        var entrega = hoy.AddHours(12);

        var pedido = await SeedServicioAsync(
            TestData.RestauranteId,
            5500m,
            entrega,
            "Bogotá",
            repartidorId: repartidor,
            nombreRepartidor: "Carlos Ramírez",
            fechaAsignacion: entrega.AddMinutes(-30),
            fechaLlegadaLocal: entrega.AddMinutes(-10),
            calificacionDomiciliario: 5,
            calificacionRestaurante: 4);

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client);
        var fila = respuesta.Pagina.Items.Should().ContainSingle().Subject;

        // Assert: la fila trae todo resuelto — el portal no cruza ni resta nada.
        fila.PedidoId.Should().Be(pedido);
        fila.Estado.Should().Be(EstadosPedido.Entregado);
        fila.CerradoEn.Should().Be(entrega);

        fila.Restaurante.Nombre.Should().Be("Demo Restaurant");
        fila.Restaurante.Ciudad.Should().Be("Bogotá");
        fila.Origen.Direccion.Should().Be("Calle 45 #23-10");

        fila.Destino.Direccion.Should().Contain("Calle 45");
        fila.Destino.Zona.Should().Be("Bogotá");

        fila.Domiciliario.Should().NotBeNull();
        fila.Domiciliario!.Id.Should().Be(repartidor);
        fila.Domiciliario.Nombre.Should().Be("Carlos Ramírez");
        fila.Domiciliario.Calificacion.Should().Be(5d);

        fila.Tiempos.AsignadoEn.Should().Be(entrega.AddMinutes(-30));
        fila.Tiempos.RecogidoEn.Should().Be(entrega.AddMinutes(-10));
        fila.Tiempos.EntregadoEn.Should().Be(entrega);
        fila.Tiempos.MinutosTotales.Should().Be(30);

        fila.Valor.Domicilio.Should().Be(5500m);
        fila.Valor.Total.Should().Be(55500m);
        fila.Valor.Paga.Should().BeNull("todavía no hay fuente para quién paga el domicilio");

        fila.Calificaciones.Domiciliario.Should().Be(5d);
        fila.Calificaciones.Restaurante.Should().Be(4d);

        fila.Incidencia.Hay.Should().BeFalse();
        fila.Incidencia.Motivo.Should().BeNull();

        // El KPI de tiempo promedio tiene que decir lo mismo que la fila: son el mismo cálculo.
        respuesta.Resumen.TiempoPromedioMin.Should().Be(30);
        respuesta.Resumen.CompletadosHoy.Should().Be(respuesta.Pagina.Total);
    }

    [Fact]
    public async Task ServiciosFinalizados_SinAsignacion_LosTiemposYElDomiciliarioVanEnNull()
    {
        // Arrange: un pedido entregado sin asignación registrada (no pasó por Delivery).
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var entrega = hoy.AddHours(10);
        await SeedServicioAsync(TestData.RestauranteId, 5000m, entrega, "Bogotá");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client);
        var fila = respuesta.Pagina.Items.Should().ContainSingle().Subject;

        // Assert: lo que falta viaja en null, no en cero ni en una fecha vacía.
        fila.Domiciliario.Should().BeNull();
        fila.Tiempos.AsignadoEn.Should().BeNull();
        fila.Tiempos.RecogidoEn.Should().BeNull();
        fila.Tiempos.EntregadoEn.Should().Be(entrega);
        fila.Tiempos.MinutosTotales.Should().BeNull("falta un extremo del cálculo: no viaja en 0");
        fila.Calificaciones.Domiciliario.Should().BeNull();
        fila.Calificaciones.Restaurante.Should().BeNull();

        respuesta.Resumen.CompletadosHoy.Should().Be(1);
        respuesta.Resumen.TiempoPromedioMin.Should().BeNull("un promedio que no se puede calcular no es 0");
        respuesta.Resumen.CalificacionPromedio.Should().BeNull();
        respuesta.Resumen.CalificacionesContadas.Should().Be(0);
    }

    [Fact]
    public async Task ServiciosFinalizados_CalificacionPromedio_CuentaSoloLasCalificadas()
    {
        // Arrange: dos servicios del mismo domiciliario con notas distintas, y uno sin encuesta.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var repartidor = Guid.NewGuid();

        await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(10), "Bogotá",
            repartidorId: repartidor, nombreRepartidor: "Carlos Ramírez",
            calificacionDomiciliario: 5, calificacionRestaurante: 5);
        await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá",
            repartidorId: repartidor, nombreRepartidor: "Carlos Ramírez",
            calificacionDomiciliario: 3, calificacionRestaurante: 1);
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(12), "Bogotá");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client);

        // Assert: la nota del servicio es la del restaurante (RatingGeneralComida), la misma
        // que promedia el panel de métricas. Promediar los dos sujetos daría 3,5: un número
        // que no significa nada.
        respuesta.Resumen.CalificacionPromedio.Should().Be(3d);
        respuesta.Resumen.CalificacionesContadas.Should().Be(2,
            "el 'basado en N calificaciones' son las encuestas, no los servicios");

        // La calificación de la columna es el promedio histórico del domiciliario (sus dos
        // notas: 4), no la del pedido que muestra esa fila.
        respuesta.Pagina.Items
            .Select(f => f.Domiciliario?.Calificacion)
            .Should().BeEquivalentTo(new double?[] { 4d, 4d, null });

        respuesta.Resumen.CompletadosHoy.Should().Be(respuesta.Pagina.Total);
    }

    [Fact]
    public async Task ServiciosFinalizados_Ayer_SeCalculaConLosMismosFiltros()
    {
        // Arrange: dos servicios hoy y tres ayer en Bogotá, más uno ayer en Medellín.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var ayer = hoy.AddDays(-1);

        await SeedServicioAsync(TestData.RestauranteId, 3000m, hoy.AddHours(10), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 1000m, ayer.AddHours(10), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 2000m, ayer.AddHours(11), "Bogotá");
        await SeedServicioAsync(TestData.RestauranteId, 3000m, ayer.AddHours(12), "Bogotá");

        var medellin = await SeedRestauranteEnCiudadAsync("Medellín", "Sabor Paisa");
        await SeedServicioAsync(medellin, 9999m, ayer.AddHours(13), "Medellín");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client, "?zona=" + Uri.EscapeDataString("bogotá"));

        // Assert: ayer corre el mismo agregado con los mismos filtros y la ventana corrida
        // un día; si no, la variación compararía cosas distintas.
        respuesta.Resumen.CompletadosHoy.Should().Be(2);
        respuesta.Resumen.ValorDomiciliosHoy.Should().Be(8000m);
        respuesta.Resumen.Ayer.Completados.Should().Be(3);
        respuesta.Resumen.Ayer.ValorDomicilios.Should().Be(6000m);
    }

    [Fact]
    public async Task ServiciosFinalizados_FiltraPorDomiciliario()
    {
        // Arrange: dos domiciliarios distintos y un servicio sin asignación.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var carlos = Guid.NewGuid();
        var esperado = await SeedServicioAsync(
            TestData.RestauranteId, 3000m, hoy.AddHours(10), "Bogotá",
            repartidorId: carlos, nombreRepartidor: "Carlos Ramírez");
        await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá",
            repartidorId: Guid.NewGuid(), nombreRepartidor: "Ana Gómez");
        await SeedServicioAsync(TestData.RestauranteId, 7000m, hoy.AddHours(12), "Bogotá");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client, $"?repartidorId={carlos}");

        // Assert: el filtro tiene que coincidir con lo que la fila muestra, y el KPI también.
        respuesta.Pagina.Total.Should().Be(1);
        respuesta.Pagina.Items.Should().ContainSingle().Which.PedidoId.Should().Be(esperado);
        respuesta.Resumen.CompletadosHoy.Should().Be(1);
        respuesta.Resumen.ValorDomiciliosHoy.Should().Be(3000m);
    }

    [Fact]
    public async Task ServiciosFinalizados_FiltraPorCalificacionMinima()
    {
        // Arrange: notas cruzadas a propósito. El bien calificado es el que tiene 5 de
        // restaurante y 2 de domiciliario: si el filtro mirara al domiciliario, entraría el otro.
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var bienCalificado = await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(10), "Bogotá",
            repartidorId: Guid.NewGuid(), nombreRepartidor: "Carlos Ramírez",
            calificacionDomiciliario: 2, calificacionRestaurante: 5);
        await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá",
            repartidorId: Guid.NewGuid(), nombreRepartidor: "Ana Gómez",
            calificacionDomiciliario: 5, calificacionRestaurante: 2);
        // Un servicio sin encuesta no alcanza ninguna mínima: no se lo inventa.
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(12), "Bogotá");

        using var client = CreateAdminClient();

        // Act
        var respuesta = await GetAsync(client, "?calificacionMin=4");

        // Assert
        respuesta.Pagina.Total.Should().Be(1);
        respuesta.Pagina.Items.Should().ContainSingle().Which.PedidoId.Should().Be(bienCalificado);
        respuesta.Resumen.CompletadosHoy.Should().Be(1);
        respuesta.Resumen.CalificacionesContadas.Should().Be(1);
    }

    [Fact]
    public async Task ServiciosFinalizados_BuscaPorPedidoClienteODireccion()
    {
        // Arrange
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        var buscado = await SeedServicioAsync(
            TestData.RestauranteId, 5000m, hoy.AddHours(10), "Bogotá",
            nombreCliente: "Ana María Torres",
            codigo: "PED-20260925-000777",
            calle: "Carrera 70");
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(11), "Bogotá");

        using var client = CreateAdminClient();

        // Act + Assert: el texto libre busca en el pedido, el cliente y la dirección, y el
        // KPI se recorta con el mismo texto que las filas.
        foreach (var termino in new[] { "000777", "Ana María", "Carrera 70" })
        {
            var respuesta = await GetAsync(client, "?buscar=" + Uri.EscapeDataString(termino));

            respuesta.Pagina.Items.Should().ContainSingle().Which.PedidoId.Should().Be(buscado);
            respuesta.Resumen.CompletadosHoy.Should().Be(1);
        }

        var sinResultados = await GetAsync(client, "?buscar=" + Uri.EscapeDataString("no-existe-xyz"));

        sinResultados.Pagina.Total.Should().Be(0);
        sinResultados.Resumen.CompletadosHoy.Should().Be(0);
        sinResultados.Resumen.ValorDomiciliosHoy.Should().Be(0m);
    }

    [Fact]
    public async Task ServiciosFinalizados_FiltrosSinFuente_Devuelven400()
    {
        // Arrange
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(10), "Bogotá");
        using var client = CreateAdminClient();

        // Act + Assert: un filtro sin fuente no puede devolver un conjunto vacío silencioso,
        // que en pantalla se lee como "no hubo incidencias" o "nadie pagó el domicilio".
        var incidencia = await client.GetAsync($"{Ruta}?conIncidencia=true");
        incidencia.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var pago = await client.GetAsync($"{Ruta}?pago=Cliente");
        pago.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Un valor fuera de los dos canónicos también es un error de la petición.
        var pagoInvalido = await client.GetAsync($"{Ruta}?pago=Otro");
        pagoInvalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ServiciosFinalizados_CalificacionMinimaFueraDeRango_Devuelve400()
    {
        // Arrange
        await ResetDatabaseAsync();
        var hoy = DateTime.UtcNow.Date;
        await SeedServicioAsync(TestData.RestauranteId, 5000m, hoy.AddHours(10), "Bogotá");
        using var client = CreateAdminClient();

        // Act: las notas son 1..5, así que un 9 no puede devolver una lista vacía en silencio.
        var response = await client.GetAsync($"{Ruta}?calificacionMin=9");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ServiciosFinalizados_SinRolAdmin_Devuelve403()
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
