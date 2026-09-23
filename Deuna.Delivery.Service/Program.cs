using Deuna.Delivery.Service.Consumers;
using Deuna.Delivery.Service.Endpoints;
using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Shared.Extensions;
using Deuna.Shared.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

Log.Information("Starting Deuna Delivery Service");

builder.Services.AddOpenApi();

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

builder.Services.AddDbContext<DeliveryDbContext>(options =>
{
    if (useInMemory)
    {
        options.UseInMemoryDatabase("deuna_delivery");
        return;
    }

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5434;Database=deuna_delivery;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql =>
    {
        // Requerido para mapear propiedades NetTopologySuite (Point) a geography
        npgsql.UseNetTopologySuite();
        npgsql.MigrationsAssembly("Deuna.Delivery.Service");
    });
});

// Redis: se registra si hay configuración explícita; en tests (BD InMemory) permite
// apuntar a un Redis real en TestContainers.
var redisConfiguration = builder.Configuration["Redis:Configuration"]
    ?? (useInMemory ? null : "localhost:6379,password=redis_dev_2026");

if (redisConfiguration is not null)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfiguration));
}

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PedidoCreadoConsumer>();

    if (useInMemory)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("delivery", false));
        });
        return;
    }

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(RabbitMqConnection.BuildUri(builder.Configuration));

        // Prefijo por servicio en los nombres de cola. Sin esto, todos los servicios que
        // consumen el mismo tipo de mensaje comparten la MISMA cola (MassTransit la nombra
        // con el tipo) y se roban los eventos entre sí: cada mensaje lo recibe uno solo.
        cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("delivery", false));
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Autenticación JWT compartida (policies: restaurant, rider, admin)
builder.Services.AddDeunaJwtAuthentication(builder.Configuration);

// Tracking GPS
builder.Services.AddScoped<ITrackingStore, RedisTrackingStore>();
builder.Services.AddScoped<ITrackingService, TrackingService>();

var rabbitUri = RabbitMqConnection.BuildUri(builder.Configuration);
var redisConn = builder.Configuration["Redis:Configuration"] ?? "localhost:6379,password=redis_dev_2026";

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DeliveryDbContext>();

if (!useInMemory)
{
    builder.Services.AddHealthChecks()
        .AddRedis(redisConn)
        .AddRabbitMQ(sp =>
        {
            // Se usa la URI real de configuración: el placeholder con contraseña enmascarada
            // y host localhost hacía que el health check fallara siempre dentro del contenedor.
            var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = rabbitUri };
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

app.MapGet("/api/delivery/health", () => Results.Ok(new { status = "healthy", service = "delivery", timestamp = DateTime.UtcNow }))
    .WithName("DeliveryHealthCheck")
    .AllowAnonymous();

// Tracking GPS (TASK-302)
app.MapTrackingEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
    if (useInMemory)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}

Log.Information("Deuna Delivery Service started successfully");
await app.RunAsync();

public partial class Program { }
