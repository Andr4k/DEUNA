using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Deuna.Feedback.Service.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.CalificacionesRestaurantes;

/// <summary>
/// El agregado por restaurante de la pantalla de calificaciones (sección 4.1 del plan):
/// promedio, total, distribución como conteos, aspectos agrupados por el criterio tal como
/// llegó, nombre y zona resueltos contra la réplica de restaurantes, y tendencia contra el
/// período anterior.
/// </summary>
public class CalificacionesPorRestauranteTests : IAsyncLifetime
{
    private static readonly DateTime Inicio = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private TestWebApplicationFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static FiltroCalificacionesRestaurantes Rango => new(Desde: Inicio, Hasta: Fin);

    private async Task<PaginaCalificacionesRestaurantes> ConsultarAsync(FiltroCalificacionesRestaurantes? filtro = null)
    {
        using var scope = _factory.Services.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ICalificacionesRestaurantesService>();
        return await servicio.ObtenerCalificacionesAsync(filtro ?? Rango);
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
    public async Task AgrupaPorRestaurante_ConElPromedioYLaDistribucionEnConteos()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.AddRange(
                Encuesta(pizza, 5, Inicio.AddDays(1)),
                Encuesta(pizza, 5, Inicio.AddDays(2)),
                Encuesta(pizza, 4, Inicio.AddDays(3)),
                Encuesta(pizza, 3, Inicio.AddDays(4)));
            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync();

        pagina.Total.Should().Be(1);
        var fila = pagina.Items.Should().ContainSingle().Which;
        fila.TotalCalificaciones.Should().Be(4);
        fila.CalificacionPromedio.Should().BeApproximately(4.25, 0.0001);
        // Conteos, no porcentajes: el porcentaje lo calcula la vista.
        fila.Distribucion.Should().Be(new DistribucionDeEstrellas(Cinco: 2, Cuatro: 1, Tres: 1, Dos: 0, Una: 0));
    }

    [Fact]
    public async Task SinCalificacionesEnElRango_NoHayFilaQueMostrar()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            // Fuera del rango: no cuenta.
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Fin.AddDays(5)));
            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync();

        pagina.Total.Should().Be(0);
        pagina.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ElNombreYLaZona_SalenDeLaReplicaDeRestaurantes()
    {
        var sushi = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(sushi, "Sushi Nikkei", "Bogotá"));
            db.FeedbackEncuestas.Add(Encuesta(sushi, 4, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var fila = (await ConsultarAsync()).Items.Should().ContainSingle().Which;

        fila.Nombre.Should().Be("Sushi Nikkei");
        fila.Zona.Should().Be("Bogotá");
    }

    [Fact]
    public async Task TipoDeComidaEIncidencias_ViajanEnNull()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var fila = (await ConsultarAsync()).Items.Should().ContainSingle().Which;

        // No hay fuente de tipo de comida ni modelo de incidencias: null, no 0 ni cadena vacía.
        fila.TipoDeComida.Should().BeNull();
        fila.Incidencias.Should().BeNull();
    }

    [Fact]
    public async Task LosAspectos_SeAgrupanPorElCriterioTalComoLlego()
    {
        var pizza = Guid.NewGuid();
        var primera = Encuesta(pizza, 5, Inicio.AddDays(1));
        var segunda = Encuesta(pizza, 4, Inicio.AddDays(2));

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.AddRange(primera, segunda);
            // El criterio es texto libre (TASK-402): "Sabor" y "sabor" son dos criterios
            // distintos. No se normalizan ni se fuerza un conjunto canónico.
            db.FeedbackDetallesCriterios.AddRange(
                new FeedbackDetalleCriterio { EncuestaId = primera.Id, Criterio = "Sabor", Puntaje = 5 },
                new FeedbackDetalleCriterio { EncuestaId = segunda.Id, Criterio = "sabor", Puntaje = 3 },
                new FeedbackDetalleCriterio { EncuestaId = primera.Id, Criterio = "Empaque", Puntaje = 4 },
                new FeedbackDetalleCriterio { EncuestaId = segunda.Id, Criterio = "Empaque", Puntaje = 2 });
            await db.SaveChangesAsync();
        });

        var fila = (await ConsultarAsync()).Items.Should().ContainSingle().Which;

        fila.Aspectos.Should().HaveCount(3);
        fila.Aspectos.Should().ContainSingle(a => a.Criterio == "Sabor" && a.Promedio == 5 && a.Cantidad == 1);
        fila.Aspectos.Should().ContainSingle(a => a.Criterio == "sabor" && a.Promedio == 3 && a.Cantidad == 1);
        fila.Aspectos.Should().ContainSingle(a => a.Criterio == "Empaque" && a.Promedio == 3 && a.Cantidad == 2);
    }

    [Fact]
    public async Task SinPeriodoAnterior_LaTendenciaEsNull()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var fila = (await ConsultarAsync()).Items.Should().ContainSingle().Which;

        // Sin base no hay variación: "sin dato", no -100%.
        fila.Tendencia.Variacion.Should().BeNull();
    }

    [Fact]
    public async Task ConPeriodoAnterior_LaTendenciaComparaContraEsePromedio()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            // Período anterior: promedio 4. Período actual: promedio 5. Sube 25%.
            db.FeedbackEncuestas.Add(Encuesta(pizza, 4, Inicio.AddDays(-10)));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var fila = (await ConsultarAsync()).Items.Should().ContainSingle().Which;

        fila.CalificacionPromedio.Should().Be(5);
        fila.Tendencia.Variacion.Should().BeApproximately(25, 0.01);
    }

    [Fact]
    public async Task ElFiltroPorZona_AcotaLasFilas()
    {
        var medellin = Guid.NewGuid();
        var bogota = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(medellin, "Pizza Roma", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(bogota, "Sushi Nikkei", "Bogotá"));
            db.FeedbackEncuestas.Add(Encuesta(medellin, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(bogota, 4, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync(new FiltroCalificacionesRestaurantes(Desde: Inicio, Hasta: Fin, Zona: "Bogotá"));

        pagina.Total.Should().Be(1);
        pagina.Items.Should().ContainSingle().Which.Nombre.Should().Be("Sushi Nikkei");
    }

    [Fact]
    public async Task ElFiltroPorNombre_BuscaEnLaReplica()
    {
        var pizza = Guid.NewGuid();
        var sushi = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(sushi, "Sushi Nikkei", "Bogotá"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(sushi, 4, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync(new FiltroCalificacionesRestaurantes(Desde: Inicio, Hasta: Fin, Buscar: "sushi"));

        pagina.Total.Should().Be(1);
        pagina.Items.Should().ContainSingle().Which.Nombre.Should().Be("Sushi Nikkei");
    }

    [Fact]
    public async Task ElFiltroPorRangoDeCalificacion_UsaElPromedioDelRestaurante()
    {
        var bueno = Guid.NewGuid();
        var malo = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(bueno, "Bueno", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(malo, "Malo", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(bueno, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(malo, 2, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync(new FiltroCalificacionesRestaurantes(Desde: Inicio, Hasta: Fin, CalificacionMin: 4));

        pagina.Total.Should().Be(1);
        pagina.Items.Should().ContainSingle().Which.Nombre.Should().Be("Bueno");
    }

    [Fact]
    public async Task LaPagina_RespetaElTamanoYDejaElTotalDelConjunto()
    {
        await EnLaBaseAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                db.RestaurantesReplicados.Add(Restaurante(id, $"Restaurante {i}", "Medellín"));
                db.FeedbackEncuestas.Add(Encuesta(id, 5 - i, Inicio.AddDays(1)));
            }

            await db.SaveChangesAsync();
        });

        var pagina = await ConsultarAsync(new FiltroCalificacionesRestaurantes(Desde: Inicio, Hasta: Fin, Pagina: 1, Tamano: 2));

        pagina.Items.Should().HaveCount(2);
        pagina.Total.Should().Be(3);
        pagina.Pagina.Should().Be(1);
        pagina.Tamano.Should().Be(2);
    }
}
