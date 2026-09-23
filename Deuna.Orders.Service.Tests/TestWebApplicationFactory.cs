using Deuna.Orders.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Deuna.Orders.Service.Tests;

/// <summary>
/// Levanta la API real de Orders contra un PostGIS en contenedor.
///
/// Se usa el motor real (no InMemory) porque <c>GeoService</c> ejecuta SQL de PostGIS
/// (<c>geo.distance_meters</c>, <c>geo.point_in_polygon</c>) que el proveedor InMemory
/// no puede resolver, y porque así los tests corren contra el mismo motor que producción.
///
/// La mensajería queda en memoria: los tests no necesitan RabbitMQ.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestWebApplicationFactory()
    {
        // Program.cs lee estos flags al inicio, antes de que WebApplicationFactory aplique
        // ConfigureAppConfiguration. Por eso van como variables de entorno: son lo único
        // que la configuración ya ve en ese momento.
        Environment.SetEnvironmentVariable("UseInMemoryDatabase", "false");
        Environment.SetEnvironmentVariable("UseInMemoryMessaging", "true");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", PostgisTestContainer.ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwt.SecretKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwt.Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwt.Audience);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = PostgisTestContainer.ConnectionString,
                ["UseInMemoryDatabase"] = "false",
                ["UseInMemoryMessaging"] = "true",
                ["Jwt:SecretKey"] = TestJwt.SecretKey,
                ["Jwt:Issuer"] = TestJwt.Issuer,
                ["Jwt:Audience"] = TestJwt.Audience,
            });
        });
    }
}
