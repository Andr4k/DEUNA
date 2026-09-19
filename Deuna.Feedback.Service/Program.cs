using Deuna.Feedback.Service.Models;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

Log.Information("Starting Deuna Feedback Service");

builder.Services.AddOpenApi();

builder.Services.AddDbContext<FeedbackDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5435;Database=deuna_feedback;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Deuna.Feedback.Service"));
});

builder.Services.AddMassTransit(x =>
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
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

ConfigureHealthChecks(builder.Services, builder.Configuration);

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
    await db.Database.MigrateAsync();
}

Log.Information("Deuna Feedback Service started successfully");
await app.RunAsync();

static void ConfigureHealthChecks(IServiceCollection services, IConfiguration configuration)
{
    var rabbitConn = configuration.GetConnectionString("RabbitMQ") ?? "amqp://deuna:rabbitmq_dev_2026@localhost:5672/deuna";
    services.AddHealthChecks()
        .AddDbContextCheck<FeedbackDbContext>()
        .AddRabbitMQ(rabbitConnectionString: rabbitConn);
}

public partial class Program { }
