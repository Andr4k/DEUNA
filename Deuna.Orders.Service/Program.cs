using Deuna.Orders.Service.Consumers;
using Deuna.Orders.Service.Endpoints;
using Deuna.Orders.Service.Models;
using Deuna.Orders.Service.Services;
using Deuna.Orders.Service.Validators;
using Deuna.Shared.Extensions;
using Deuna.Shared.Messaging;
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

// Los flags se leen UNA sola vez y antes de registrar la base de datos.
// El proveedor de BD y el transporte de mensajería son decisiones independientes:
// los tests usan PostGIS real (Testcontainers) con mensajería en memoria, mientras
// que producción usa Postgres + RabbitMQ. Registrar ambos proveedores de EF en el
// mismo service provider es un error, por eso la elección es excluyente.
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);
var useInMemoryMessaging = builder.Configuration.GetValue<bool>("UseInMemoryMessaging", false);

builder.Services.AddDbContext<OrdersDbContext>(options =>
{
    if (useInMemoryDatabase)
    {
        options.UseInMemoryDatabase("OrdersTest");
        return;
    }

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
    // Replicación de restaurantes desde Identity (necesaria para validar sus pedidos)
    x.AddConsumer<RestauranteRegistradoConsumer>();
    // Cierra el pedido cuando Delivery confirma la entrega (TASK-305).
    x.AddConsumer<PedidoEntregadoConsumer>();

    if (useInMemoryMessaging)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("orders", false));
        });
    }
    else
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(RabbitMqConnection.BuildUri(builder.Configuration));

            // Prefijo por servicio: evita compartir cola con otros servicios que consuman
            // el mismo tipo de mensaje.
            cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("orders", false));
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

var rabbitUri = RabbitMqConnection.BuildUri(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>();

if (!useInMemoryMessaging)
{
    builder.Services.AddHealthChecks()
        .AddRabbitMQ(sp =>
        {
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

app.MapGet("/api/orders/health", () => Results.Ok(new { status = "healthy", service = "orders", timestamp = DateTime.UtcNow }))
    .WithName("OrdersHealthCheck")
    .AllowAnonymous();

// Map Orders Endpoints
app.MapOrdersEndpoints();

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    if (!useInMemoryDatabase)
    {
        // Migrate (no EnsureCreated) para que también se apliquen las funciones
        // SQL de PostGIS (geo.distance_meters, geo.point_in_polygon).
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