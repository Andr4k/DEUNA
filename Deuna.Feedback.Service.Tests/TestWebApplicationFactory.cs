using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Deuna.Feedback.Service.Tests;

/// <summary>
/// Arranca Feedback Service con EF InMemory y transporte InMemory de MassTransit,
/// sin depender de PostgreSQL ni RabbitMQ.
///
/// Se usan variables de entorno (no ConfigureAppConfiguration) porque Program.cs lee
/// "UseInMemoryDatabase" en las sentencias de nivel superior, antes de que
/// WebApplicationFactory aplique su colección de configuración.
///
/// Cada instancia usa un nombre de BD InMemory único: el store se comparte por nombre
/// en todo el proceso, así que un nombre fijo hacía que los tests se pisaran entre sí
/// (se observó un fallo intermitente antes de aislarlo).
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _inMemoryDatabaseName = $"feedback-tests-{Guid.NewGuid()}";

    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "InMemory");
        Environment.SetEnvironmentVariable("InMemoryDatabaseName", _inMemoryDatabaseName);
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
            Environment.SetEnvironmentVariable("InMemoryDatabaseName", null);
        }
    }
}
