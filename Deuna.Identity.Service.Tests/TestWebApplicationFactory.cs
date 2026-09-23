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
                // Un almacén InMemory por instancia de factory (una por clase de test). Va
                // aquí y no en una variable de entorno porque el entorno es global del
                // proceso: todas las factories leerían el mismo nombre y volverían a
                // compartir datos. Program.cs lee esta clave dentro del lambda diferido de
                // AddDbContext, así que ya ve la configuración de esta instancia.
                ["InMemoryDatabaseName"] = $"identity-tests-{Guid.NewGuid():N}",
                ["Jwt:SecretKey"] = JwtSecret,
                ["Jwt:Issuer"] = "Deuna.Test",
                ["Jwt:Audience"] = "Deuna.Test",
            };
            config.AddInMemoryCollection(inMemorySettings!);
        });

        builder.ConfigureServices(services =>
        {
            // El DbContext ya lo registra Program.cs en modo InMemory (condicional al flag),
            // así que no hay ningún descriptor de Npgsql que retirar. El borrado por nombre
            // que había aquí no alcanzaba a los servicios internos del proveedor y era
            // exactamente lo que dejaba dos proveedores registrados.
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable("UseInMemoryDatabase", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("InMemoryDatabaseName", null);
            Environment.SetEnvironmentVariable("Jwt__SecretKey", null);
            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
        }
    }
}
