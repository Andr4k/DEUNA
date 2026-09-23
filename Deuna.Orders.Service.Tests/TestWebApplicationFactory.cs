using Deuna.Orders.Service;
using Deuna.Orders.Service.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deuna.Orders.Service.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "InMemory",
                ["UseInMemoryDatabase"] = "true",
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:Username"] = "guest",
                ["RabbitMQ:Password"] = "guest",
                ["RabbitMQ:VirtualHost"] = "/",
                ["Jwt:SecretKey"] = "TestSecretKeyForTestingPurposesOnly123456789",
                ["Jwt:Issuer"] = "Deuna.Test",
                ["Jwt:Audience"] = "Deuna.Test",
            };
            config.AddInMemoryCollection(inMemorySettings!);
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<OrdersDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Remove Npgsql-related options
            var npgsqlDescriptors = services.Where(d =>
                d.ServiceType.FullName?.Contains("Npgsql") == true ||
                d.ServiceType.FullName?.Contains("MigrationsAssembly") == true
            ).ToList();
            foreach (var d in npgsqlDescriptors) services.Remove(d);

            // Add InMemory database for testing
            services.AddDbContext<OrdersDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestOrdersDb");
            });

            // DO NOT BuildServiceProvider here - let WebApplicationFactory do it
        });
    }
}