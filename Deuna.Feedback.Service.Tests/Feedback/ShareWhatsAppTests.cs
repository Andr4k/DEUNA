using System.Net;
using System.Net.Http.Json;
using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.Feedback;

/// <summary>
/// Viralidad por WhatsApp (US-004.3, FR-004.5): el enlace corto se genera solo con
/// rating alto y el compartido queda registrado.
/// </summary>
public class ShareWhatsAppTests : IAsyncLifetime
{
    private const string UrlNivel1 = "/api/v1/feedback/submit-basic";
    private const string UrlCompartir = "/api/v1/feedback/share-whatsapp";

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

    private async Task<PedidoReplicado> SembrarPedidoConNivel1Async(int ratingComida = 5, string nombreRestaurante = "Pizzería La Esquina")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();

        var restauranteId = Guid.NewGuid();
        db.RestaurantesReplicados.Add(new RestauranteReplicado
        {
            Id = restauranteId,
            NombreComercial = nombreRestaurante
        });

        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-TEST-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = restauranteId,
            RepartidorId = Guid.NewGuid(),
            Estado = PedidoReplicado.EstadoEntregado,
            Total = 55000m
        };

        db.PedidosReplicados.Add(pedido);
        await db.SaveChangesAsync();

        var basico = await _client.PostAsJsonAsync(UrlNivel1,
            new SubmitBasicFeedbackRequest(pedido.PedidoId, ratingComida, 4, ratingComida >= 4));
        basico.StatusCode.Should().Be(HttpStatusCode.OK);

        return pedido;
    }

    private async Task<HttpResponseMessage> CompartirAsync(Guid pedidoId)
        => await _client.PostAsJsonAsync(UrlCompartir, new ShareWhatsAppRequest(pedidoId));

    // ------------------------------------------------------------------ camino feliz

    [Fact]
    public async Task ConRatingAlto_GeneraElEnlaceYRegistraElCompartido()
    {
        var pedido = await SembrarPedidoConNivel1Async();

        var response = await CompartirAsync(pedido.PedidoId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = (await response.Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!;
        cuerpo.Enlace.Should().NotBeNullOrWhiteSpace();
        cuerpo.YaCompartido.Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var encuesta = await db.FeedbackEncuestas.SingleAsync(e => e.PedidoId == pedido.PedidoId);
        var compartido = await db.CompartidosWhatsApp.SingleAsync(c => c.EncuestaId == encuesta.Id);

        compartido.FueCompartido.Should().BeTrue();
        compartido.FechaCompartido.Should().NotBeNull();
        compartido.CodigoCompartido.Should().NotBeNullOrWhiteSpace();
        cuerpo.Enlace.Should().Contain(compartido.CodigoCompartido);
    }

    [Fact]
    public async Task ElMensajeIncluyeElNombreDelRestauranteYElEnlace()
    {
        // US-004.3: el mensaje prellenado nombra al restaurante y lleva el enlace.
        var pedido = await SembrarPedidoConNivel1Async(nombreRestaurante: "Pizzería La Esquina");

        var response = await CompartirAsync(pedido.PedidoId);

        var cuerpo = (await response.Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!;
        cuerpo.Mensaje.Should().Contain("Pizzería La Esquina");
        cuerpo.Mensaje.Should().Contain(cuerpo.Enlace);
    }

    [Fact]
    public async Task ElEnlaceEsCorto()
    {
        // US-004.3 pide un enlace *corto*: un GUID de 36 caracteres no lo es.
        var pedido = await SembrarPedidoConNivel1Async();

        var response = await CompartirAsync(pedido.PedidoId);

        var cuerpo = (await response.Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!;
        var codigo = cuerpo.Enlace.Split('/').Last();
        codigo.Should().HaveLength(8);
    }

    [Fact]
    public async Task CompartirDosVeces_DevuelveElMismoEnlaceSinDuplicar()
    {
        // El cliente puede tocar el botón dos veces: el segundo intento no puede crear
        // otro compartido ni cambiar el enlace que ya mandó.
        var pedido = await SembrarPedidoConNivel1Async();

        var primero = (await (await CompartirAsync(pedido.PedidoId)).Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!;
        var response = await CompartirAsync(pedido.PedidoId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var segundo = (await response.Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!;

        segundo.Enlace.Should().Be(primero.Enlace);
        segundo.YaCompartido.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.CompartidosWhatsApp.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DosEncuestasDistintas_GeneranEnlacesDistintos()
    {
        var primero = await SembrarPedidoConNivel1Async();
        var segundo = await SembrarPedidoConNivel1Async();

        var enlace1 = (await (await CompartirAsync(primero.PedidoId)).Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!.Enlace;
        var enlace2 = (await (await CompartirAsync(segundo.PedidoId)).Content.ReadFromJsonAsync<ShareWhatsAppResponse>())!.Enlace;

        enlace1.Should().NotBe(enlace2);
    }

    // ------------------------------------------------------------------ rechazos

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    public async Task ConRatingBajo_Devuelve400(int ratingComida)
    {
        // FR-004.5: con menos de 4 estrellas el botón no aparece, así que el endpoint
        // tampoco puede aceptar el compartido.
        var pedido = await SembrarPedidoConNivel1Async(ratingComida);

        var response = await CompartirAsync(pedido.PedidoId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.CompartidosWhatsApp.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SinNivel1_Devuelve404()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = "PED-SIN-ENCUESTA",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Estado = PedidoReplicado.EstadoEntregado,
            Total = 1000m
        };
        db.PedidosReplicados.Add(pedido);
        await db.SaveChangesAsync();

        var response = await CompartirAsync(pedido.PedidoId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConPedidoInexistente_Devuelve404()
    {
        var response = await CompartirAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
