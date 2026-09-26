using Deuna.Feedback.Service.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Feedback.Service.Tests;

/// <summary>
/// Arranca Feedback Service con EF InMemory y transporte InMemory de MassTransit,
/// sin depender de PostgreSQL ni RabbitMQ.
///
/// Las banderas que Program.cs lee en las sentencias de nivel superior van por variables de
/// entorno (no ConfigureAppConfiguration): la configuración del builder ya está armada cuando
/// WebApplicationFactory puede intervenir.
///
/// Cada instancia usa un nombre de BD InMemory único, y ese nombre NO puede ir por variable de
/// entorno: la variable es del proceso y el host se construye de forma diferida, así que dos
/// factories en paralelo se pisaban el nombre —y el Dispose de una borraba el de la otra— y las
/// dos terminaban sobre la MISMA base. Con la base compartida, los tests que cuentan filas
/// (calificaciones, restaurantes) veían datos de la otra clase y fallaban por contaminación,
/// no por lógica. El nombre va por instancia en ConfigureServices, que corre después de las
/// registraciones de Program y no depende del orden en que los tests construyan su host.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _inMemoryDatabaseName = $"feedback-tests-{Guid.NewGuid()}";

    public TestWebApplicationFactory()
    {
        // Estas tres son iguales para toda factory: no hay carrera entre ellas.
        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "InMemory");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Program.cs registra el DbContext leyendo "InMemoryDatabaseName" de su configuración,
        // que ya está armada cuando el factory puede intervenir. Reemplazar la registración acá
        // es lo que fija el nombre por instancia sin carrera.
        builder.ConfigureServices(services =>
        {
            var registradas = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<FeedbackDbContext>))
                .ToList();

            foreach (var registrada in registradas)
            {
                services.Remove(registrada);
            }

            services.AddDbContext<FeedbackDbContext>(options =>
                options.UseInMemoryDatabase(_inMemoryDatabaseName));
        });
    }
}
