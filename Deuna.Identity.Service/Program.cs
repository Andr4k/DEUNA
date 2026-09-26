using BCrypt.Net;
using Deuna.Identity.Service.Endpoints;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Services;
using Deuna.Shared.Extensions;
using Deuna.Shared.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOpenApi();

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);
        
    if (useInMemory)
    {
        // El nombre es configurable a propósito: EF InMemory mantiene UN almacén por
        // nombre para todo el proceso, así que un nombre fijo hace que las clases de
        // test compartan datos y que un EnsureDeleted de una borre lo de las demás.
        var inMemoryName = builder.Configuration["InMemoryDatabaseName"] ?? "IdentityTest";
        options.UseInMemoryDatabase(inMemoryName);
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=deuna_identity;Username=deuna_user;Password=deuna_dev_2026";
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Deuna.Identity.Service"));
    }
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

// Register Auth Service
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuthorizationCodeService, AuthorizationCodeService>();
builder.Services.AddScoped<IGestionUsuariosService, GestionUsuariosService>();

// Register Validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var rabbitUri = RabbitMqConnection.BuildUri(builder.Configuration);
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>();

if (!useInMemory)
{
    builder.Services.AddHealthChecks()
        .AddRabbitMQ(sp => 
        {
            var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = rabbitUri };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });
}

// Rate limiting: 5 requests per minute per IP for login (always register services, disable in test)
builder.Services.AddRateLimiter(options =>
{
    // 429 es la semántica correcta para "demasiadas peticiones"; el default de ASP.NET es 503.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var isTestEnvironment = builder.Environment.IsEnvironment("Testing") || 
        builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);
    
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = isTestEnvironment ? int.MaxValue : 5; // Disable in test
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

// El logger estatico de Serilog solo queda enganchado cuando el host esta construido:
// una llamada a Log.X antes de Build() cae a un logger silencioso y se pierde.
app.Logger.LogInformation("Starting Deuna Identity Service");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Use shared JWT middleware (after UseAuthentication/UseAuthorization)
app.UseDeunaJwtMiddleware();

app.MapHealthChecks("/health");

app.MapGet("/api/identity/health", () => Results.Ok(new { status = "healthy", service = "identity", timestamp = DateTime.UtcNow }))
    .WithName("IdentityHealthCheck")
    .AllowAnonymous();

// Map Auth Endpoints
app.MapAuthEndpoints();

// Endpoints admin del portal (gestión de usuarios)
app.MapUsuariosAdminEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
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

app.Logger.LogInformation("Deuna Identity Service started successfully");
await app.RunAsync();

public partial class Program { }