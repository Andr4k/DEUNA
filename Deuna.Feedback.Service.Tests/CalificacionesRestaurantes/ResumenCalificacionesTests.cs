using Deuna.Feedback.Service.DTOs;
using Deuna.Feedback.Service.Models;
using Deuna.Feedback.Service.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests.CalificacionesRestaurantes;

/// <summary>
/// El resumen de la pantalla de calificaciones (sección 4.2 del plan): los KPIs de la
/// cabecera calculados sobre el MISMO conjunto filtrado que devuelve la lista, no con
/// consultas aparte.
/// </summary>
public class ResumenCalificacionesTests : IAsyncLifetime
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

    private async Task<ResumenCalificacionesRestaurantes> ResumirAsync(FiltroCalificacionesRestaurantes? filtro = null)
    {
        using var scope = _factory.Services.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ICalificacionesRestaurantesService>();
        return await servicio.ObtenerResumenAsync(filtro ?? Rango);
    }

    private async Task<PaginaCalificacionesRestaurantes> ConsultarAsync(FiltroCalificacionesRestaurantes filtro)
    {
        using var scope = _factory.Services.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ICalificacionesRestaurantesService>();
        return await servicio.ObtenerCalificacionesAsync(filtro);
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
    public async Task SinCalificaciones_ElPromedioGlobalEsNull_NoCero()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Fin.AddDays(10)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        // Un promedio de cero y un promedio que no existe se ven igual en pantalla y
        // significan cosas opuestas.
        resumen.PromedioGlobal.Should().BeNull();
        resumen.TotalCalificaciones.Should().Be(0);
        resumen.RestaurantesCalificados.Should().Be(0);
        resumen.Distribucion.Should().Be(new DistribucionDeEstrellas(0, 0, 0, 0, 0));
        resumen.Top.Should().BeEmpty();
    }

    [Fact]
    public async Task RestaurantesTotales_CuentaLosActivos_NoLosCalificados()
    {
        var calificado = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(calificado, "Calificado", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(Guid.NewGuid(), "Activo sin calificar", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(Guid.NewGuid(), "Otro activo", "Bogotá"));
            db.RestaurantesReplicados.Add(Restaurante(Guid.NewGuid(), "Inactivo", "Bogotá", activo: false));
            db.FeedbackEncuestas.Add(Encuesta(calificado, 5, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        // El KPI dice "1 de 3": el denominador es el universo de activos, no los calificados.
        resumen.RestaurantesCalificados.Should().Be(1);
        resumen.RestaurantesTotales.Should().Be(3);
    }

    [Fact]
    public async Task ElResumen_SeCalculaSobreElMismoConjuntoFiltradoQueLasFilas()
    {
        var medellin = Guid.NewGuid();
        var bogota = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(medellin, "Pizza Roma", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(bogota, "Sushi Nikkei", "Bogotá"));
            db.FeedbackEncuestas.Add(Encuesta(medellin, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(medellin, 5, Inicio.AddDays(2)));
            db.FeedbackEncuestas.Add(Encuesta(bogota, 2, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var filtro = new FiltroCalificacionesRestaurantes(Desde: Inicio, Hasta: Fin, Zona: "Medellín");

        var pagina = await ConsultarAsync(filtro);
        var resumen = await ResumirAsync(filtro);

        // Las filas y los KPIs tienen que contar lo mismo: si el resumen usara su propia
        // consulta, acá aparecerían las tres calificaciones en vez de las dos de Medellín.
        pagina.Items.Should().ContainSingle().Which.Nombre.Should().Be("Pizza Roma");
        resumen.RestaurantesCalificados.Should().Be(pagina.Items.Count);
        resumen.TotalCalificaciones.Should().Be(2);
        resumen.PromedioGlobal.Should().BeApproximately(5, 0.0001);
        resumen.Distribucion.Should().Be(new DistribucionDeEstrellas(Cinco: 2, Cuatro: 0, Tres: 0, Dos: 0, Una: 0));
    }

    [Fact]
    public async Task ElPromedioGlobal_PonderaTodasLasCalificaciones()
    {
        var primero = Guid.NewGuid();
        var segundo = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(primero, "Primero", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(segundo, "Segundo", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(primero, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(primero, 5, Inicio.AddDays(2)));
            db.FeedbackEncuestas.Add(Encuesta(segundo, 2, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        // (5 + 5 + 2) / 3 = 4. Promediar los promedios de los restaurantes daría 3.5.
        resumen.PromedioGlobal.Should().BeApproximately(4, 0.0001);
        resumen.TotalCalificaciones.Should().Be(3);
    }

    [Fact]
    public async Task LaDistribucion_SumaLosConteosDeTodosLosRestaurantes()
    {
        var primero = Guid.NewGuid();
        var segundo = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(primero, "Primero", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(segundo, "Segundo", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(primero, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(primero, 5, Inicio.AddDays(2)));
            db.FeedbackEncuestas.Add(Encuesta(segundo, 4, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(segundo, 3, Inicio.AddDays(2)));
            db.FeedbackEncuestas.Add(Encuesta(segundo, 1, Inicio.AddDays(3)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        resumen.Distribucion.Should().Be(new DistribucionDeEstrellas(Cinco: 2, Cuatro: 1, Tres: 1, Dos: 0, Una: 1));
    }

    [Fact]
    public async Task LaEvolucion_TraeUnPuntoPorDia()
    {
        var pizza = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, Inicio.AddDays(1).AddHours(3)));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 4, Inicio.AddDays(1).AddHours(9)));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 2, Inicio.AddDays(2).AddHours(1)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        resumen.Evolucion.Should().HaveCount(2);
        resumen.Evolucion[0].Fecha.Should().Be(Inicio.AddDays(1).Date);
        resumen.Evolucion[0].Promedio.Should().BeApproximately(4.5, 0.0001);
        resumen.Evolucion[1].Fecha.Should().Be(Inicio.AddDays(2).Date);
        resumen.Evolucion[1].Promedio.Should().BeApproximately(2, 0.0001);
    }

    [Fact]
    public async Task DestacadosYAlerta_UsanLosUmbralesDelPlan()
    {
        var destacado = Guid.NewGuid();
        var alerta = Guid.NewGuid();
        var medio = Guid.NewGuid();

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(destacado, "Destacado", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(alerta, "Alerta", "Medellín"));
            db.RestaurantesReplicados.Add(Restaurante(medio, "Medio", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(destacado, 5, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(alerta, 3, Inicio.AddDays(1)));
            db.FeedbackEncuestas.Add(Encuesta(medio, 4, Inicio.AddDays(1)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        resumen.Destacados.Should().Be(new DestacadosDelResumen(Minimo: 4.7, Cantidad: 1));
        resumen.EnAlerta.Should().Be(new EnAlertaDelResumen(Maximo: 3.5, Cantidad: 1));
    }

    [Fact]
    public async Task ElTop_TraeLosCincoConMayorPromedio()
    {
        await EnLaBaseAsync(async db =>
        {
            for (var i = 0; i < 6; i++)
            {
                var id = Guid.NewGuid();
                db.RestaurantesReplicados.Add(Restaurante(id, $"Restaurante {i}", "Medellín"));
                // Calificaciones dentro del 1..5 que exige el esquema (check constraint): el
                // sexto restaurante repite el 5. Así hay seis candidatos, el tope es 5 y el
                // peor (Restaurante 0) queda fuera del top.
                db.FeedbackEncuestas.Add(Encuesta(id, Math.Min(i + 1, 5), Inicio.AddDays(1)));
            }

            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync();

        resumen.Top.Should().HaveCount(5);
        resumen.Top.Should().BeInDescendingOrder(r => r.Promedio);
        resumen.Top[0].Promedio.Should().Be(6 - 1);
        resumen.Top.Should().NotContain(r => r.Nombre == "Restaurante 0");
    }

    [Fact]
    public async Task CalificacionesHoy_CuentaSoloLasDeHoy()
    {
        var pizza = Guid.NewGuid();
        var ahora = DateTime.UtcNow;

        await EnLaBaseAsync(async db =>
        {
            db.RestaurantesReplicados.Add(Restaurante(pizza, "Pizza Roma", "Medellín"));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 5, ahora));
            db.FeedbackEncuestas.Add(Encuesta(pizza, 4, ahora.AddDays(-1)));
            await db.SaveChangesAsync();
        });

        var resumen = await ResumirAsync(new FiltroCalificacionesRestaurantes(Desde: ahora.Date.AddDays(-3)));

        resumen.TotalCalificaciones.Should().Be(2);
        resumen.CalificacionesHoy.Should().Be(1);
    }
}
