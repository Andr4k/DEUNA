using Deuna.Metrics.Service.Models;
using Microsoft.Extensions.Options;

namespace Deuna.Metrics.Service.Services;

/// <summary>Rango de tiempo en UTC, ya resuelto.</summary>
public readonly record struct RangoTiempo(DateTime Desde, DateTime Hasta);

/// <summary>
/// Resuelve qué significa "hoy" para el panel.
///
/// No se usa UTC a secas: en Bogotá son las 19:00 del 24 cuando en UTC ya es el 25,
/// así que un `FechaCreacion >= current_date` en UTC dejaría los pedidos de la tarde
/// fuera del día. El día del panel es el día del operador, y de ahí para abajo todo
/// se compara en UTC.
/// </summary>
public sealed class RelojDelPanel(IOptions<MetricasOptions> opciones)
{
    private readonly TimeZoneInfo _zona = ResolverZona(opciones.Value.ZonaHoraria);

    /// <summary>El día en curso, desde las 00:00 locales hasta ahora.</summary>
    public RangoTiempo Hoy()
    {
        var ahoraLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zona);
        var inicioLocal = ahoraLocal.Date;

        return new RangoTiempo(
            TimeZoneInfo.ConvertTimeToUtc(inicioLocal, _zona),
            TimeZoneInfo.ConvertTimeToUtc(inicioLocal.AddDays(1), _zona));
    }

    /// <summary>El día anterior completo, para calcular la variación.</summary>
    public RangoTiempo Ayer()
    {
        var hoy = Hoy();
        return new RangoTiempo(hoy.Desde.AddDays(-1), hoy.Desde);
    }

    private static TimeZoneInfo ResolverZona(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(nombre);
        }
        catch (TimeZoneNotFoundException)
        {
            // Sin `tzdata` en la imagen no hay zonas horarias y toda métrica del día
            // quedaría corrida. Es mejor negarse a arrancar que servir números que
            // parecen bien y están mal.
            throw new InvalidOperationException(
                $"No se encontró la zona horaria '{nombre}'. La imagen necesita el paquete `tzdata`.");
        }
    }
}
