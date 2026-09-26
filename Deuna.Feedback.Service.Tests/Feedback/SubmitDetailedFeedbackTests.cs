using System.Net;
using System.Net.Http.Json;
using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.Feedback;

/// <summary>
/// Feedback Nivel 2 (US-004.2, FR-004.2 a FR-004.4): criterios detallados opcionales,
/// guardados como filas para poder agregar criterios sin tocar el esquema.
/// </summary>
public class SubmitDetailedFeedbackTests : IAsyncLifetime
{
    private const string UrlNivel1 = "/api/v1/feedback/submit-basic";
    private const string UrlNivel2 = "/api/v1/feedback/submit-detailed";

    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ utilidades

    private async Task<PedidoReplicado> SembrarPedidoAsync(int ratingComida = 5)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();

        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-TEST-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            RepartidorId = Guid.NewGuid(),
            Estado = PedidoReplicado.EstadoEntregado,
            Total = 55000m
        };

        db.PedidosReplicados.Add(pedido);
        await db.SaveChangesAsync();

        // El Nivel 2 solo existe sobre un Nivel 1 ya registrado (US-004.2).
        var basico = await _client.PostAsJsonAsync(UrlNivel1,
            new SubmitBasicFeedbackRequest(pedido.PedidoId, ratingComida, 4, ratingComida >= 4));
        basico.StatusCode.Should().Be(HttpStatusCode.OK);

        return pedido;
    }

    private static SubmitDetailedFeedbackRequest Peticion(
        Guid pedidoId,
        int puntaje = 5,
        string? comentario = null,
        string? fotoUrl = null)
        => new(
            PedidoId: pedidoId,
            Criterios:
            [
                new CriterioDetalladoDto("Sabor", puntaje),
                new CriterioDetalladoDto("Temperatura", puntaje),
                new CriterioDetalladoDto("Presentacion", puntaje)
            ],
            Comentario: comentario,
            FotoUrl: fotoUrl);

    // ------------------------------------------------------------------ camino feliz
    [Fact]
    public async Task ConNivel1Registrado_GuardaCadaCriterioComoUnaFila()
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = (await response.Content.ReadFromJsonAsync<SubmitDetailedFeedbackResponse>())!;
        cuerpo.Mensaje.Should().NotBeNullOrWhiteSpace();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var encuesta = await db.FeedbackEncuestas.SingleAsync(e => e.PedidoId == pedido.PedidoId);
        var criterios = await db.FeedbackDetallesCriterios
            .Where(c => c.EncuestaId == encuesta.Id)
            .ToListAsync();

        // Una fila por criterio (FR-004.3), no una columna por criterio.
        criterios.Should().HaveCount(3);
        criterios.Select(c => c.Criterio).Should().BeEquivalentTo("Sabor", "Temperatura", "Presentacion");
        criterios.Should().OnlyContain(c => c.Puntaje == 5);
    }

    [Fact]
    public async Task UnCriterioQueNoEstaEnLaSpec_SeAceptaSinTocarElEsquema()
    {
        // FR-004.4: la tabla debe permitir agregar criterios sin alterar el esquema.
        // Si los criterios fueran columnas, este envío no tendría dónde guardarse.
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, new SubmitDetailedFeedbackRequest(
            PedidoId: pedido.PedidoId,
            Criterios: [new CriterioDetalladoDto("Rapidez", 4)],
            Comentario: null,
            FotoUrl: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.FeedbackDetallesCriterios.AnyAsync(c => c.Criterio == "Rapidez")).Should().BeTrue();
    }

    [Fact]
    public async Task ConComentarioYFoto_SeGuardanEnSuPropiaTabla()
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2,
            Peticion(pedido.PedidoId, comentario: "Llegó caliente y bien empacado", fotoUrl: "https://cdn.deuna.test/f/abc.jpg"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var encuesta = await db.FeedbackEncuestas.SingleAsync(e => e.PedidoId == pedido.PedidoId);
        var detalle = await db.FeedbackComentariosFotos.SingleAsync(d => d.EncuestaId == encuesta.Id);

        detalle.ComentarioTexto.Should().Be("Llegó caliente y bien empacado");
        detalle.UrlFotoEvidencia.Should().Be("https://cdn.deuna.test/f/abc.jpg");
    }

    [Fact]
    public async Task SinComentarioNiFoto_NoCreaLaFilaOpcional()
    {
        // FR-004.2: el Nivel 2 es opcional, y dentro de él el comentario y la foto también.
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.FeedbackComentariosFotos.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    public async Task ConRatingSuficiente_DevuelveLaBanderaDelBoton(int ratingComida)
    {
        // FR-004.5: el botón de WhatsApp aparece con rating_general_comida >= 4.
        var pedido = await SembrarPedidoAsync(ratingComida);

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = (await response.Content.ReadFromJsonAsync<SubmitDetailedFeedbackResponse>())!;
        cuerpo.PuedeCompartirWhatsApp.Should().BeTrue();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public async Task ConRatingBajo_Devuelve400YNoGuardaNada(int ratingComida)
    {
        // El Nivel 2 es para quien quiere recomendar el restaurante (US-004.2): con menos de
        // 4 estrellas la PWA ni lo ofrece, así que el endpoint tampoco lo acepta.
        var pedido = await SembrarPedidoAsync(ratingComida);

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.FeedbackDetallesCriterios.CountAsync()).Should().Be(0);
    }

    // ------------------------------------------------------------------ rechazos

    [Fact]
    public async Task SinNivel1_Devuelve400()
    {
        // US-004.2: el Nivel 2 llega después del Nivel 1.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = "PED-SIN-NIVEL1",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Estado = PedidoReplicado.EstadoEntregado,
            Total = 1000m
        };
        db.PedidosReplicados.Add(pedido);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConPedidoInexistente_Devuelve404()
    {
        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnviadoDosVeces_Devuelve400YNoDuplicaLosCriterios()
    {
        var pedido = await SembrarPedidoAsync();
        (await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId, puntaje: 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.FeedbackDetallesCriterios.CountAsync()).Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task ConPuntajeFueraDeRango_Devuelve400(int puntaje)
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, Peticion(pedido.PedidoId, puntaje));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SinCriterios_Devuelve400()
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, new SubmitDetailedFeedbackRequest(
            PedidoId: pedido.PedidoId, Criterios: [], Comentario: "solo un comentario", FotoUrl: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConCriterioVacio_Devuelve400()
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, new SubmitDetailedFeedbackRequest(
            PedidoId: pedido.PedidoId,
            Criterios: [new CriterioDetalladoDto("   ", 5)],
            Comentario: null,
            FotoUrl: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConElMismoCriterioRepetido_Devuelve400()
    {
        // Dos puntajes para el mismo criterio no significan nada: sería un dato ambiguo.
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(UrlNivel2, new SubmitDetailedFeedbackRequest(
            PedidoId: pedido.PedidoId,
            Criterios: [new CriterioDetalladoDto("Sabor", 5), new CriterioDetalladoDto("Sabor", 1)],
            Comentario: null,
            FotoUrl: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
