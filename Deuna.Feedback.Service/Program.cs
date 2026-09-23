using Deuna.Feedback.Service.Consumers;
using Deuna.Feedback.Service.Endpoints;
using Deuna.Feedback.Service.Models;
using Deuna.Feedback.Service.Services;
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

Log.Information("Starting Deuna Feedback Service");

builder.Services.AddOpenApi();

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

builder.Services.AddDbContext<FeedbackDbContext>(options =>
{
    if (useInMemory)
    {
        // El nombre es configurable: el store InMemory se comparte por nombre en todo el
        // proceso, así que los tests usan uno único por factory para aislarse entre sí.
        var inMemoryName = builder.Configuration["InMemoryDatabaseName"] ?? "deuna_feedback";
        options.UseInMemoryDatabase(inMemoryName);
        return;
    }

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5435;Database=deuna_feedback;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Deuna.Feedback.Service"));
});

builder.Services.AddMassTransit(x =>
{
    // Proyección local de pedidos (valida existencia y estado antes de aceptar feedback)
    x.AddConsumer<PedidoCreadoConsumer>();
    x.AddConsumer<PedidoAsignadoConsumer>();
    x.AddConsumer<PedidoActualizadoConsumer>();
    // Evento terminal: es el que habilita la encuesta (TASK-305).
    x.AddConsumer<PedidoEntregadoConsumer>();
    x.AddConsumer<RepartidorRegistradoConsumer>();

    if (useInMemory)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("feedback", false));
        });
        return;
    }

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(RabbitMqConnection.BuildUri(builder.Configuration));

        // Prefijo por servicio en los nombres de cola. Sin esto, Delivery y Feedback
        // consumen de la MISMA cola `PedidoCreado` y cada evento lo recibe uno solo.
        cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter("feedback", false));
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

ConfigureHealthChecks(builder.Services, builder.Configuration, useInMemory);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.MapHealthChecks("/health");

app.MapGet("/api/feedback/health", () => Results.Ok(new { status = "healthy", service = "feedback", timestamp = DateTime.UtcNow }))
    .WithName("FeedbackHealthCheck")
    .AllowAnonymous();

// Feedback Nivel 1 (TASK-401)
app.MapFeedbackEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
    if (useInMemory)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}

Log.Information("Deuna Feedback Service started successfully");
await app.RunAsync();

static void ConfigureHealthChecks(IServiceCollection services, IConfiguration configuration, bool useInMemory)
{
    services.AddHealthChecks().AddDbContextCheck<FeedbackDbContext>();

    if (useInMemory)
    {
        return;
    }

    var rabbitUri = RabbitMqConnection.BuildUri(configuration);
    services.AddHealthChecks()
        .AddRabbitMQ(sp =>
        {
            // URI real de configuración: el placeholder enmascarado apuntaba a localhost
            // con contraseña `***`, así que el health check fallaba siempre en el contenedor.
            var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = rabbitUri };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });
}

public partial class Program { }
