using Deuna.Identity.Service;
using Deuna.Identity.Service.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Identity.Service.Tests;

/// <summary>
/// Arranca Identity con EF InMemory y transporte InMemory de MassTransit.
///
/// Los flags se inyectan por variables de entorno (no con ConfigureAppConfiguration):
/// Program.cs los lee en las sentencias de nivel superior, antes de que WebApplicationFactory
/// aplique su colección de configuración. Con ConfigureAppConfiguration el flag llegaba tarde,
/// el servicio arrancaba con el transporte de RabbitMQ (`guest@localhost`) y los tests pasaban
/// solo porque ningún camino publicaba eventos todavía.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string JwtSecret = "TestSecretKeyForTestingPurposesOnly123456789";

    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "InMemory");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Jwt__SecretKey", JwtSecret);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "Deuna.Test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "Deuna.Test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "InMemory",
                ["UseInMemoryDatabase"] = "true",
                ["Jwt:SecretKey"] = JwtSecret,
                ["Jwt:Issuer"] = "Deuna.Test",
                ["Jwt:Audience"] = "Deuna.Test",
            };
            config.AddInMemoryCollection(inMemorySettings!);
        });

        builder.ConfigureServices(services =>
        {
            // El DbContext ya lo registra Program.cs en modo InMemory; se garantiza que no
            // quede ningún descriptor de Npgsql apuntando a una base real.
            var npgsqlDescriptors = services.Where(d =>
                d.ServiceType.FullName?.Contains("Npgsql") == true ||
                d.ServiceType.FullName?.Contains("MigrationsAssembly") == true
            ).ToList();
            foreach (var d in npgsqlDescriptors) services.Remove(d);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable("UseInMemoryDatabase", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("Jwt__SecretKey", null);
            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
        }
    }
}
