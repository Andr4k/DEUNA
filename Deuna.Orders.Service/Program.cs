using Deuna.Orders.Service.Endpoints;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.Services;
using Deuna.Orders.Service.Validators;
using Deuna.Shared.Extensions;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

Log.Information("Starting Deuna Orders Service");

builder.Services.AddOpenApi();

builder.Services.AddDbContext<OrdersDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5433;Database=deuna_orders;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.UseNetTopologySuite();
        npgsql.MigrationsAssembly("Deuna.Orders.Service");
    });
});

builder.Services.AddMassTransit(x =>
{
    var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

    if (useInMemory)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context);
        });
    }
    else
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            var host = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            var port = builder.Configuration.GetValue<int>("RabbitMQ:Port", 5672);
            var username = builder.Configuration["RabbitMQ:Username"] ?? "deuna";
            var password = builder.Configuration["RabbitMQ:Password"] ?? "rabbitmq_dev_2026";
            var vhost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "deuna";

            var uri = new Uri($"amqp://{username}:{password}@{host}:{port}/{vhost}");
            cfg.Host(uri);

            cfg.ConfigureEndpoints(context);
        });
    }
});

// Use shared JWT authentication
builder.Services.AddDeunaJwtAuthentication(builder.Configuration);

// Register services
builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddScoped<ITarifaService, TarifaService>();
builder.Services.AddScoped<IGeoService, GeoService>();

// Register Validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var rabbitConn = builder.Configuration.GetConnectionString("RabbitMQ") ?? "amqp://deuna:***@localhost:5672/deuna";
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>();

if (!useInMemory)
{
    builder.Services.AddHealthChecks()
        .AddRabbitMQ(sp =>
        {
            var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri(rabbitConn) };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseDeunaJwtMiddleware();

app.MapHealthChecks("/health");

app.MapGet("/api/orders/health", () => Results.Ok(new { status = "healthy", service = "orders", timestamp = DateTime.UtcNow }))
    .WithName("OrdersHealthCheck")
    .AllowAnonymous();

// Map Orders Endpoints
app.MapOrdersEndpoints();

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    var useInMemoryDb = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);
    if (!useInMemoryDb)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

Log.Information("Deuna Orders Service started successfully");
await app.RunAsync();

public partial class Program { }