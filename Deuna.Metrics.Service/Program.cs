using Deuna.Metrics.Service.Endpoints;
using Deuna.Metrics.Service.Models;
using Deuna.Metrics.Service.Services;
using Deuna.Shared.Extensions;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// `Log.X` estático antes de `builder.Build()` se pierde: hay que usar `app.Logger`.
builder.Services.AddOpenApi();
builder.Services.Configure<MetricasOptions>(
    builder.Configuration.GetSection(MetricasOptions.SectionName));

builder.Services.AddDeunaJwtAuthentication(builder.Configuration);

// --- Las cuatro bases, una por dominio -------------------------------------
var conexiones = builder.Configuration.GetSection("ConnectionStrings").Get<ConexionesMetricas>()
    ?? new ConexionesMetricas();
conexiones.Validar();

// Cada subservicio recibe SOLO su cadena de conexión, y se ve acá mismo cuál le
// toca. Es la forma de que una consulta de feedback no pueda terminar leyendo
// pedidos: no tiene con qué.
builder.Services.AddSingleton<RelojDelPanel>();
builder.Services.AddSingleton<IMetricasPedidos>(sp => new MetricasPedidos(
    new FuenteDatos(conexiones.Pedidos),
    sp.GetRequiredService<IOptions<MetricasOptions>>()));
builder.Services.AddSingleton<IMetricasDomiciliarios>(sp => new MetricasDomiciliarios(
    new FuenteDatos(conexiones.Domiciliarios),
    sp.GetRequiredService<IOptions<MetricasOptions>>()));
builder.Services.AddSingleton<IMetricasFeedback>(sp => new MetricasFeedback(
    new FuenteDatos(conexiones.Feedback),
    sp.GetRequiredService<IOptions<MetricasOptions>>()));
builder.Services.AddSingleton<IMetricasRestaurantes>(_ => new MetricasRestaurantes(
    new FuenteDatos(conexiones.Restaurantes)));
builder.Services.AddSingleton<IMetricasPanel, MetricasPanel>();

builder.Services.AddHealthChecks();

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

app.MapGet("/api/metrics/health", () => Results.Ok(new { status = "healthy", service = "metrics", timestamp = DateTime.UtcNow }))
    .WithName("MetricsHealthCheck")
    .AllowAnonymous();

app.MapMetricasEndpoints();

app.Logger.LogInformation("Deuna Metrics Service started successfully");
await app.RunAsync();

/// <summary>Necesario para que los tests de integración puedan referenciar el ensamblado.</summary>
public partial class Program { }
