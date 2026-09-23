using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
