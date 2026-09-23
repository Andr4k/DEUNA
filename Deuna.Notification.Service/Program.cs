using Deuna.Notification.Service.Consumers;
using Deuna.Notification.Service.Endpoints;
using Deuna.Notification.Service.Models;
using Deuna.Notification.Service.Services;
using Deuna.Shared.Extensions;
using Deuna.Shared.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

Log.Information("Starting Deuna Notification Service");

builder.Services.AddOpenApi();

// Los flags se leen una sola vez y antes de registrar la base de datos.
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);
var useInMemoryMessaging = builder.Configuration.GetValue<bool>("UseInMemoryMessaging", false);

builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    if (useInMemoryDatabase)
    {
        options.UseInMemoryDatabase("NotificationTest");
        return;
    }

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5435;Database=deuna_notification;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Deuna.Notification.Service"));
});

builder.Services.AddMassTransit(x =>
{
    // A quién notificar: el domiciliario cuando se le asigna un pedido y el restaurante
    // cuando entra uno nuevo.
    x.AddConsumer<PedidoAsignadoConsumer>();
    x.AddConsumer<PedidoCreadoConsumer>();

    if (useInMemoryMessaging)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("notification", false));
        });
        return;
    }

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(RabbitMqConnection.BuildUri(builder.Configuration));

        // Prefijo por servicio: sin esto, todos los servicios que consumen el mismo tipo
        // de mensaje comparten cola y se roban los eventos entre sí.
        cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("notification", false));
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Autenticación JWT compartida (policies: restaurant, rider, admin)
builder.Services.AddDeunaJwtAuthentication(builder.Configuration);

// Proveedor de push. Sin credenciales de FCM configuradas se usa el emisor de log, así el
// pipeline se puede correr en desarrollo y en tests sin Firebase.
builder.Services.Configure<FcmOptions>(builder.Configuration.GetSection(FcmOptions.Seccion));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IPushSender>(sp =>
{
    var opciones = sp.GetRequiredService<IOptions<FcmOptions>>().Value;

    if (opciones.EstaConfigurado)
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("fcm");
        Log.Information("Push: proveedor FCM (proyecto {ProjectId})", opciones.ProjectId);
        return new FcmPushSender(http, opciones, sp.GetRequiredService<ILogger<FcmPushSender>>());
    }

    Log.Warning("Push: FCM sin configurar, se usa el emisor de log (no se envían notificaciones reales)");
    return new LogPushSender(sp.GetRequiredService<ILogger<LogPushSender>>());
});

builder.Services.AddScoped<INotificacionService, NotificacionService>();

var rabbitUri = RabbitMqConnection.BuildUri(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>();

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

app.MapGet("/api/notifications/health", () => Results.Ok(new { status = "healthy", service = "notifications", timestamp = DateTime.UtcNow }))
    .WithName("NotificationsHealthCheck")
    .AllowAnonymous();

app.MapDeviceEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    if (useInMemoryDatabase)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}

Log.Information("Deuna Notification Service started successfully");
await app.RunAsync();

public partial class Program { }
