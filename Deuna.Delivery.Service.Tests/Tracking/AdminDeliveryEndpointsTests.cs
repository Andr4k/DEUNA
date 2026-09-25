using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Delivery.Service.Tests.Tracking;

/// <summary>
/// El camino del portal: la ruta de administración con la política "admin".
///
/// Estos tests fijan los códigos HTTP porque el portal decide con ellos qué mostrarle al
/// operador. Y fijan el motivo, que es lo que permite distinguir "el repartidor está
/// ocupado" de "está fuera del radio" sin interpretar el texto del mensaje.
/// </summary>
[Collection(RedisCollection.Name)]
public class AdminDeliveryEndpointsTests : IAsyncLifetime
{
    private const string RutaBase = "/api/v1/admin/delivery/asignar";

    private static readonly Guid RiderCercano = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid RiderLejano = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private const double LatPedido = 4.6097;
    private const double LonPedido = -74.0817;

    private readonly RedisContainerFixture _fixture;
    private TrackingWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public AdminDeliveryEndpointsTests(RedisContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _factory = new TrackingWebApplicationFactory(_fixture.ConnectionString);
        _client = _factory.CreateClient();
        await LimpiarAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>La BD InMemory se comparte por nombre dentro del proceso: se limpia entre tests.</summary>
    private async Task LimpiarAsync()
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        db.PedidosDisponibles.RemoveRange(db.PedidosDisponibles);
        db.AsignacionesRepartidor.RemoveRange(db.AsignacionesRepartidor);
        db.RepartidoresReplicados.RemoveRange(db.RepartidoresReplicados);
        await db.SaveChangesAsync();

        var tracking = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        await tracking.EliminarAsync(RiderCercano);
        await tracking.EliminarAsync(RiderLejano);
    }

    private async Task<PedidoDisponible> SembrarPedidoAsync(
        string estado = PedidoDisponible.EstadoBuscando,
        Guid? repartidorId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-ADMIN-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 55000m,
            Estado = estado,
            TokenQrLocal = Guid.NewGuid().ToString("N"),
            TokenQrEntrega = Guid.NewGuid().ToString("N"),
            Latitud = LatPedido,
            Longitud = LonPedido,
            RepartidorId = repartidorId
        };

        db.PedidosDisponibles.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    /// <summary>Telemetría del repartidor a cierta distancia al norte del pedido.</summary>
    private async Task UbicarAsync(Guid riderId, double kmAlNorte)
    {
        using var scope = _factory.Services.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<ITrackingStore>();
        await tracking.ActualizarAsync(
            riderId, LatPedido + (kmAlNorte / 111.32), LonPedido, DateTime.UtcNow);
    }

    /// <summary>El perfil del repartidor en la réplica: de ahí sale el nombre que ve el operador.</summary>
    private async Task RegistrarRepartidorAsync(Guid riderId, string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        db.RepartidoresReplicados.Add(new RepartidorReplicado
        {
            Id = riderId,
            NombreCompleto = nombre,
            CiudadOperacion = "Bogota"
        });

        await db.SaveChangesAsync();
    }

    private HttpClient ClienteConToken(string token)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private HttpClient ClienteAdmin() => ClienteConToken(
        TrackingWebApplicationFactory.CrearToken(Guid.NewGuid(), Roles.Admin));

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private async Task<(HttpStatusCode Codigo, RespuestaAsignacion? Cuerpo)> AsignarAsync(
        HttpClient cliente, Guid pedidoId, Guid repartidorId)
    {
        var respuesta = await cliente.PostAsJsonAsync(
            $"{RutaBase}/{pedidoId}", new { repartidorId });

        // Un 401 y un 403 salen SIN cuerpo: deserializar a ciegas revienta con "the input
        // does not contain any JSON tokens" y el test falla por el motivo equivocado, que es
        // peor que fallar: manda a buscar el problema donde no está.
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = string.IsNullOrWhiteSpace(texto)
            ? null
            : JsonSerializer.Deserialize<RespuestaAsignacion>(texto, JsonOpciones);

        return (respuesta.StatusCode, cuerpo);
    }

    // ------------------------------------------------------------------ acceso

    [Fact]
    public async Task SinToken_Devuelve401()
    {
        var pedido = await SembrarPedidoAsync();

        var respuesta = await _client.PostAsJsonAsync($"{RutaBase}/{pedido.PedidoId}", new { repartidorId = RiderCercano });

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConTokenDeRepartidor_Devuelve403()
    {
        // La política es "admin": un domiciliario con credenciales válidas no asigna pedidos.
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteConToken(
            TrackingWebApplicationFactory.CrearToken(Guid.NewGuid(), Roles.Rider));

        var (codigo, _) = await AsignarAsync(cliente, pedido.PedidoId, RiderCercano);

        codigo.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------- éxito

    [Fact]
    public async Task AsignaAlRepartidorElegido_Devuelve200()
    {
        await UbicarAsync(RiderCercano, 1);
        await UbicarAsync(RiderLejano, 4);
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await AsignarAsync(cliente, pedido.PedidoId, RiderLejano);

        codigo.Should().Be(HttpStatusCode.OK);
        cuerpo!.RepartidorId.Should().Be(RiderLejano, "el operador lo eligió a él");
    }

    // ---------------------------------------------------------------- rechazos

    [Fact]
    public async Task RepartidorFueraDelRadio_Devuelve409()
    {
        await UbicarAsync(RiderLejano, 20);
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await AsignarAsync(cliente, pedido.PedidoId, RiderLejano);

        codigo.Should().Be(HttpStatusCode.Conflict);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.RepartidorNoEsCandidato));
    }

    [Fact]
    public async Task RepartidorOcupado_Devuelve409ConMotivoDistinto()
    {
        // Mismo código HTTP que "fuera del radio", pero motivo distinto: para el operador
        // no es lo mismo "mandalo a otro" que "esperá a que termine".
        await UbicarAsync(RiderCercano, 1);
        await SembrarPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await AsignarAsync(cliente, pedido.PedidoId, RiderCercano);

        codigo.Should().Be(HttpStatusCode.Conflict);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.RepartidorOcupado));
    }

    [Fact]
    public async Task PedidoQueYaNoEstaEnBuscando_Devuelve400()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await SembrarPedidoAsync(PedidoDisponible.EstadoAsignado, RiderLejano);
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await AsignarAsync(cliente, pedido.PedidoId, RiderCercano);

        codigo.Should().Be(HttpStatusCode.BadRequest);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.EstadoInvalido));
    }

    [Fact]
    public async Task PedidoNoProyectado_Devuelve404()
    {
        await UbicarAsync(RiderCercano, 1);
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await AsignarAsync(cliente, Guid.NewGuid(), RiderCercano);

        codigo.Should().Be(HttpStatusCode.NotFound);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.PedidoNoEncontrado));
    }

    [Fact]
    public async Task SinRepartidor_Devuelve400DeValidacion()
    {
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var respuesta = await cliente.PostAsJsonAsync(
            $"{RutaBase}/{pedido.PedidoId}", new { repartidorId = Guid.Empty });

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    // ----------------------------------------------------------------- candidatos

    [Fact]
    public async Task Candidatos_SinToken_Devuelve401()
    {
        var pedido = await SembrarPedidoAsync();

        var respuesta = await _client.GetAsync($"/api/v1/admin/delivery/candidatos/{pedido.PedidoId}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Candidatos_ConTokenDeRepartidor_Devuelve403()
    {
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteConToken(
            TrackingWebApplicationFactory.CrearToken(Guid.NewGuid(), Roles.Rider));

        var respuesta = await cliente.GetAsync($"/api/v1/admin/delivery/candidatos/{pedido.PedidoId}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Candidatos_ListaAlLibreConSuNombreYDistancia()
    {
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Cercana");
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirCandidatosAsync(cliente, pedido.PedidoId);

        codigo.Should().Be(HttpStatusCode.OK);
        cuerpo!.Candidatos.Should().HaveCount(1);

        var candidato = cuerpo.Candidatos[0];
        candidato.RepartidorId.Should().Be(RiderCercano);
        candidato.NombreCompleto.Should().Be("Ana Cercana");
        candidato.Disponible.Should().BeTrue();
        candidato.DistanciaMetros.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Candidatos_ElOcupadoFiguraNoDisponibleConSuServicioActual()
    {
        // "No disponible" a secas no le sirve al operador: necesita saber en qué anda.
        await UbicarAsync(RiderCercano, 1);
        await RegistrarRepartidorAsync(RiderCercano, "Ana Ocupada");
        var enCurso = await SembrarPedidoAsync(PedidoDisponible.EstadoEnRuta, RiderCercano);
        var pedido = await SembrarPedidoAsync();
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirCandidatosAsync(cliente, pedido.PedidoId);

        codigo.Should().Be(HttpStatusCode.OK);

        var candidato = cuerpo!.Candidatos.Single();
        candidato.Disponible.Should().BeFalse();
        candidato.ServicioActualCodigo.Should().Be(enCurso.Codigo);
        candidato.ServicioActualEstado.Should().Be(PedidoDisponible.EstadoEnRuta);
    }

    [Fact]
    public async Task Candidatos_PedidoNoProyectado_Devuelve404()
    {
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirCandidatosAsync(cliente, Guid.NewGuid());

        codigo.Should().Be(HttpStatusCode.NotFound);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.PedidoNoEncontrado));
    }

    [Fact]
    public async Task Candidatos_PedidoYaAsignado_Devuelve400()
    {
        await UbicarAsync(RiderCercano, 1);
        var pedido = await SembrarPedidoAsync(PedidoDisponible.EstadoAsignado, RiderLejano);
        using var cliente = ClienteAdmin();

        var (codigo, cuerpo) = await PedirCandidatosAsync(cliente, pedido.PedidoId);

        codigo.Should().Be(HttpStatusCode.BadRequest);
        cuerpo!.Motivo.Should().Be(nameof(MotivoAsignacion.EstadoInvalido));
    }

    private async Task<(HttpStatusCode Codigo, RespuestaCandidatos? Cuerpo)> PedirCandidatosAsync(
        HttpClient cliente, Guid pedidoId)
    {
        var respuesta = await cliente.GetAsync($"/api/v1/admin/delivery/candidatos/{pedidoId}");
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = string.IsNullOrWhiteSpace(texto)
            ? null
            : JsonSerializer.Deserialize<RespuestaCandidatos>(texto, JsonOpciones);

        return (respuesta.StatusCode, cuerpo);
    }
}

/// <summary>Forma de la respuesta de candidatos, para poder leerla en los tests.</summary>
internal sealed record RespuestaCandidatos(
    Guid PedidoId,
    string? Codigo,
    double RadioKm,
    int Total,
    List<CandidatoCuerpo> Candidatos,
    string? Message = null,
    string? Motivo = null);

internal sealed record CandidatoCuerpo(
    Guid RepartidorId,
    string? NombreCompleto,
    string? CiudadOperacion,
    double? DistanciaMetros,
    bool Disponible,
    string? ServicioActualCodigo,
    string? ServicioActualEstado,
    DateTime? OcupadoDesde);

/// <summary>Forma de la respuesta de error, para poder leer el motivo en los tests.</summary>
internal sealed record RespuestaAsignacion(string Message, Guid? RepartidorId, string Motivo);
