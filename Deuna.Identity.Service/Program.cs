using BCrypt.Net;
using Deuna.Identity.Service.Models;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

Log.Information("Starting Deuna Identity Service");

builder.Services.AddOpenApi();

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=deuna_identity;Username=deuna_user;Password=deuna_dev_2026";
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Deuna.Identity.Service"));
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:SecretKey"] ?? "your-super-secret-key-at-least-32-characters-long";
        var issuer = builder.Configuration["Jwt:Issuer"] ?? "deuna-api";
        var audience = builder.Configuration["Jwt:Audience"] ?? "deuna-clients";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var rabbitConn = builder.Configuration.GetConnectionString("RabbitMQ") ?? "amqp://deuna:rabbitmq_dev_2026@localhost:5672/deuna";
builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>()
    .AddRabbitMQ(rabbitConnectionString: rabbitConn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGet("/api/identity/health", () => Results.Ok(new { status = "healthy", service = "identity", timestamp = DateTime.UtcNow }))
    .WithName("IdentityHealthCheck")
    .AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();
}

Log.Information("Deuna Identity Service started successfully");
await app.RunAsync();

public partial class Program { }
