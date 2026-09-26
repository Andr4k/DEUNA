using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.CalificacionesRestaurantes;

/// <summary>
/// Los tres endpoints del panel (sección 4 del plan), con el test que más importa: los KPIs del
/// resumen y las filas de la lista salen del MISMO conjunto filtrado.
///
/// Si el resumen armara su propia consulta, la cabecera y la tabla contarían cosas distintas y
/// ninguna prueba de los dos por separado lo detectaría —por eso acá se piden los DOS endpoints
/// con los mismos filtros y se comparan los números—. Es el mismo principio que quedó fijado en
/// el contrato de Servicios finalizados.
/// </summary>
public class AdminCalificacionesRestaurantesTests : IAsyncLifetime
{
    private const string Lista = "/api/v1/admin/feedback/restaurantes";
    private const string Resumen = "/api/v1/admin/feedback/restaurantes/resumen";

    private static readonly DateTime Inicio = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions JsonDelApi = new(JsonSerializerDefaults.Web);

    private TestWebApplicationFactory _factory = null!;
    private HttpClient _admin = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        _admin = ClienteAdmin();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// La ventana viaja en las dos peticiones: es parte del conjunto, no del paginado.
    /// </summary>
    private static string Ventana =>
        $"?desde={Uri.EscapeDataString(Inicio.ToString("O"))}&hasta={Uri.EscapeDataString(Fin.ToString("O"))}";

    private HttpClient ClienteAdmin()
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateAdminToken(Guid.NewGuid()));
        return cliente;
    }

    private static async Task<T> GetAsync<T>(HttpClient cliente, string ruta)
    {
        var respuesta = await cliente.GetAsync(ruta);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        // Un 401/403 no trae cuerpo: deserializar a ciegas lanzaría JsonException y el test
        // fallaría por una razón ajena a lo que prueba.
        var contenido = await respuesta.Content.ReadAsStringAsync();
        contenido.Should().NotBeNullOrWhiteSpace();

        return JsonSerializer.Deserialize<T>(contenido, JsonDelApi)!;
    }

    private async Task EnLaBaseAsync(Func<FeedbackDbContext, Task> accion)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        await accion(db);
    }

    private static FeedbackEncuesta Encuesta(Guid restauranteId, int rating, DateTime fecha) => new()
    {
        PedidoId = Guid.NewGuid(),
        RestauranteId = restauranteId,
        RatingGeneralComida = rating,
        RatingServicioRepartidor = 5,
        CreatedAt = fecha
    };

    private static RestauranteReplicado Restaurante(Guid id, string nombre, string ciudad, bool activo = true) => new()
    {
        Id = id,
        NombreComercial = nombre,
        Ciudad = ciudad,
        Activo = activo
    };

    [Fact]
    public async Task LosKpisYLasFilas_SalenDelMismoConjuntoFiltrado()
    {
        var medellin = Guid.NewGuid();
        var bogota = Guid.NewGuid();
        var cali = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.AddRange(
                Restaurante(medellin, "Pizza Roma", "Medellín"),
                Restaurante(bogota, "Sushi Nikkei", "Bogotá"),
                Restaurante(cali, "Arepas de la esquina", "Cali"));
            db.FeedbackEncuestas.AddRange(
                Encuesta(medellin, 5, Inicio.AddDays(1)),
                Encuesta(medellin, 5, Inicio.AddDays(2)),
                Encuesta(bogota, 5, Inicio.AddDays(1)),
                Encuesta(cali, 2, Inicio.AddDays(2)));
            await db.SaveChangesAsync();
        });

        var filtroDeZona = $"&zona={Uri.EscapeDataString("Medellín")}";

        // Los dos endpoints con los MISMOS filtros: es lo único que garantiza que la cabecera y
        // la tabla no cuenten cosas distintas.
        var lista = await GetAsync<PaginaCalificacionesRestaurantes>(_admin, $"{Lista}{Ventana}{filtroDeZona}");
        var resumen = await GetAsync<ResumenCalificacionesRestaurantes>(_admin, $"{Resumen}{Ventana}{filtroDeZona}");

        // El total de la página y el conteo del resumen son el mismo número, y las
        // calificaciones que cuenta el KPI son las de las filas.
        lista.Total.Should().Be(1);
        resumen.RestaurantesCalificados.Should().Be(lista.Total);
        resumen.TotalCalificaciones.Should().Be(lista.Items.Sum(item => item.TotalCalificaciones));

        // Y los KPIs miran el conjunto filtrado, no el universo: Bogotá y Cali no entran.
        resumen.PromedioGlobal.Should().BeApproximately(5, 0.0001);
        resumen.Distribucion.Should().Be(new DistribucionDeEstrellas(Cinco: 2, Cuatro: 0, Tres: 0, Dos: 0, Una: 0));
        resumen.Destacados.Cantidad.Should().Be(1);
        resumen.EnAlerta.Cantidad.Should().Be(0);
        resumen.Top.Should().ContainSingle().Which.RestauranteId.Should().Be(medellin);

        // El conjunto sin filtro sí tiene tres restaurantes: sin esta comprobación, el test
        // pasaría igual aunque el resumen ignorara el filtro, porque los datos no lo distinguirían.
        var sinFiltro = await GetAsync<ResumenCalificacionesRestaurantes>(_admin, $"{Resumen}{Ventana}");
        sinFiltro.RestaurantesCalificados.Should().Be(3);
    }

    [Fact]
    public async Task ElTotalYLosKpis_SonDelConjuntoEntero_NoDeLaPagina()
    {
        var pizza = Guid.NewGuid();
        var sushi = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.AddRange(
                Restaurante(pizza, "Pizza Roma", "Medellín"),
                Restaurante(sushi, "Sushi Nikkei", "Bogotá"));
            db.FeedbackEncuestas.AddRange(
                Encuesta(pizza, 5, Inicio.AddDays(1)),
                Encuesta(sushi, 3, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var lista = await GetAsync<PaginaCalificacionesRestaurantes>(_admin, $"{Lista}{Ventana}&pagina=1&tamano=1");
        var resumen = await GetAsync<ResumenCalificacionesRestaurantes>(_admin, $"{Resumen}{Ventana}");

        // La página trae una fila y el total sigue siendo el del conjunto: calcular el KPI sobre
        // lo visible es la otra forma en que la cabecera y la tabla divergen.
        lista.Items.Should().HaveCount(1);
        lista.Total.Should().Be(2);
        resumen.RestaurantesCalificados.Should().Be(2);
        resumen.TotalCalificaciones.Should().Be(2);
    }

    [Fact]
    public async Task LasRutasDelPanel_ExigenLaPoliticaAdmin()
    {
        // Sin token, 401.
        var sinToken = _factory.CreateClient();
        (await sinToken.GetAsync($"{Lista}{Ventana}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Con un token de restaurante —que sí sirve para enviar encuestas en /api/v1/feedback—
        // el panel responde 403: es la razón de que el grupo sea nuevo.
        var deRestaurante = _factory.CreateClient();
        deRestaurante.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateRestaurantToken(Guid.NewGuid()));
        (await deRestaurante.GetAsync($"{Lista}{Ventana}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await deRestaurante.GetAsync($"{Resumen}{Ventana}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Y el token de administrador entra.
        (await _admin.GetAsync($"{Lista}{Ventana}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElDetalle_DevuelveLaFilaDelRestaurante()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.AddRange(
                Encuesta(pizza, 5, Inicio.AddDays(1)),
                Encuesta(pizza, 3, Inicio.AddDays(2)));
            await db.SaveChangesAsync();
        });

        var fila = await GetAsync<CalificacionDeRestaurante>(_admin, $"{Lista}/{pizza}{Ventana}");

        fila.RestauranteId.Should().Be(pizza);
        fila.Nombre.Should().Be("Pizza Roma");
        fila.CalificacionPromedio.Should().BeApproximately(4, 0.0001);
        fila.TotalCalificaciones.Should().Be(2);
        fila.Distribucion.Should().Be(new DistribucionDeEstrellas(Cinco: 1, Cuatro: 0, Tres: 1, Dos: 0, Una: 0));
    }

    [Fact]
    public async Task ElDetalle_SinCalificacionesEnLaVentana_Devuelve404()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            // Fuera de la ventana: no hay fila que mostrar.
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Fin.AddDays(5)));
            await db.SaveChangesAsync();
        });

        var respuesta = await _admin.GetAsync($"{Lista}/{pizza}{Ventana}");

        // 404 y no una fila con el promedio en null: eso se leería como "sacó cero".
        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LasPeticionesInvalidasYLosFiltrosSinFuente_SeRechazan()
    {
        // El tipo de comida no existe en ningún servicio (sección 2 del plan): se rechaza en vez
        // de ignorarlo, porque un filtro aceptado y no aplicado devuelve el conjunto entero.
        (await _admin.GetAsync($"{Lista}{Ventana}&tipoDeComida=Italiana")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await _admin.GetAsync($"{Resumen}{Ventana}&tipoDeComida=Italiana")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        // El único orden de la lista es el promedio; aceptar otro valor devolvería la misma
        // lista como si hubiera reordenado.
        (await _admin.GetAsync($"{Lista}{Ventana}&orden=nombre")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        // Sin ventana, la lista y el resumen no pueden salir del mismo conjunto.
        (await _admin.GetAsync(Lista)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _admin.GetAsync($"{Resumen}{Ventana}&calificacionMin=9")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await _admin.GetAsync($"{Lista}{Ventana}&pagina=0")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        // El valor de orden que sí se honra pasa.
        (await _admin.GetAsync($"{Lista}{Ventana}&orden=promedio")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }
}
