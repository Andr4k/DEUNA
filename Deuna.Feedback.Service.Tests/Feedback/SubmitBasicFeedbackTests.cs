using System.Net;
using System.Net.Http.Json;
using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.Feedback;

public class SubmitBasicFeedbackTests : IAsyncLifetime
{
    private const string Url = "/api/v1/feedback/submit-basic";

    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        // Cada factory usa una BD InMemory única (ver TestWebApplicationFactory),
        // así que no hace falta limpiar tablas entre tests.
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

    private async Task<PedidoReplicado> SembrarPedidoAsync(string estado = PedidoReplicado.EstadoEntregado, Guid? repartidorId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();

        var pedido = new PedidoReplicado
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-TEST-{Random.Shared.Next(100000, 999999)}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            RepartidorId = repartidorId,
            Estado = estado,
            Total = 55000m
        };

        db.PedidosReplicados.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    private async Task<SubmitBasicFeedbackResponse> EnviarAsync(Guid pedidoId, int comida = 5, int repartidor = 4, bool recomendar = true)
    {
        var response = await _client.PostAsJsonAsync(Url, new SubmitBasicFeedbackRequest(pedidoId, comida, repartidor, recomendar));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SubmitBasicFeedbackResponse>())!;
    }

    [Fact]
    public async Task ConPedidoEntregado_RegistraElFeedback()
    {
        var repartidorId = Guid.NewGuid();
        var pedido = await SembrarPedidoAsync(repartidorId: repartidorId);

        var respuesta = await EnviarAsync(pedido.PedidoId, comida: 5, repartidor: 4, recomendar: true);

        respuesta.EncuestaId.Should().NotBeEmpty();
        respuesta.Mensaje.Should().NotBeNullOrWhiteSpace();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var encuesta = await db.FeedbackEncuestas.SingleAsync(e => e.PedidoId == pedido.PedidoId);
        encuesta.RatingGeneralComida.Should().Be(5);
        encuesta.RatingServicioRepartidor.Should().Be(4);
        encuesta.DeseaRecomendar.Should().BeTrue();
        encuesta.RestauranteId.Should().Be(pedido.RestauranteId);
        encuesta.RepartidorId.Should().Be(repartidorId);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, true)]
    [InlineData(3, false)]
    [InlineData(1, false)]
    public async Task MostrarNivel2_DependeDelRatingDeComida(int ratingComida, bool esperado)
    {
        var pedido = await SembrarPedidoAsync();

        var respuesta = await EnviarAsync(pedido.PedidoId, comida: ratingComida);

        respuesta.MostrarNivel2.Should().Be(esperado);
    }

    [Fact]
    public async Task ConPedidoNoEntregado_Devuelve400()
    {
        var pedido = await SembrarPedidoAsync(estado: "EnRuta");

        var response = await _client.PostAsJsonAsync(Url, new SubmitBasicFeedbackRequest(pedido.PedidoId, 5, 4, true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConPedidoInexistente_Devuelve404()
    {
        var response = await _client.PostAsJsonAsync(Url, new SubmitBasicFeedbackRequest(Guid.NewGuid(), 5, 4, true));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConFeedbackYaRegistrado_Devuelve400()
    {
        var pedido = await SembrarPedidoAsync();
        await EnviarAsync(pedido.PedidoId);

        var response = await _client.PostAsJsonAsync(Url, new SubmitBasicFeedbackRequest(pedido.PedidoId, 3, 3, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        (await db.FeedbackEncuestas.CountAsync(e => e.PedidoId == pedido.PedidoId)).Should().Be(1);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(6, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 9)]
    public async Task ConRatingsFueraDeRango_Devuelve400(int comida, int repartidor)
    {
        var pedido = await SembrarPedidoAsync();

        var response = await _client.PostAsJsonAsync(Url, new SubmitBasicFeedbackRequest(pedido.PedidoId, comida, repartidor, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
