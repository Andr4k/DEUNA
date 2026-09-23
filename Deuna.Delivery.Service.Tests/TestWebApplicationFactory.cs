using Deuna.Delivery.Service.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deuna.Delivery.Service.Tests;

/// <summary>
/// Arranca Delivery Service con EF InMemory + transporte InMemory de MassTransit,
/// sin depender de PostgreSQL, Redis ni RabbitMQ.
///
/// Se usa una variable de entorno (no ConfigureAppConfiguration) porque el Program.cs
/// lee "UseInMemoryDatabase" en las sentencias de nivel superior, antes de que
/// WebApplicationFactory aplique su colección de configuración.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "InMemory");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Sin Redis no hay telemetría: se reemplaza el store real por un doble para que
            // el grafo sea resoluble. El matching con Redis real se prueba en Assignment/.
            services.RemoveAll<ITrackingStore>();
            services.AddScoped<ITrackingStore, TrackingStoreNulo>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable("UseInMemoryDatabase", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        }
    }
}
