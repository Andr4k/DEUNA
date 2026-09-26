namespace Deuna.Metrics.Service.Models;

/// <summary>
/// Las cuatro bases que consulta el servicio de métricas.
///
/// Una cadena de conexión por dominio, y cada subservicio usa solo la suya. Eso
/// es lo que hace que agregar un dominio nuevo (por ejemplo pagos) sea agregar
/// una propiedad acá y un subservicio, sin tocar los existentes.
///
/// Se leen de la sección `ConnectionStrings` para que el compose las inyecte por
/// variable de entorno como en el resto de los servicios.
/// </summary>
public sealed class ConexionesMetricas
{
    public string Pedidos { get; set; } = string.Empty;
    public string Domiciliarios { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public string Restaurantes { get; set; } = string.Empty;

    /// <summary>
    /// Falla al arrancar si falta una conexión, en lugar de fallar en la primera
    /// petición cuando alguien ya está mirando el panel.
    /// </summary>
    public void Validar()
    {
        var faltantes = new List<string>();

        if (string.IsNullOrWhiteSpace(Pedidos)) faltantes.Add(nameof(Pedidos));
        if (string.IsNullOrWhiteSpace(Domiciliarios)) faltantes.Add(nameof(Domiciliarios));
        if (string.IsNullOrWhiteSpace(Feedback)) faltantes.Add(nameof(Feedback));
        if (string.IsNullOrWhiteSpace(Restaurantes)) faltantes.Add(nameof(Restaurantes));

        if (faltantes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Faltan cadenas de conexión en ConnectionStrings: {string.Join(", ", faltantes)}");
        }
    }
}

/// <summary>
/// Parámetros de cálculo de las métricas.
///
/// Están en configuración y no en el código porque son decisiones de negocio: el
/// día que exista el tiempo prometido por pedido, `MinutosParaConsiderarATiempo`
/// se reemplaza por ese dato y nadie tiene que recompilar para ajustarlo.
/// </summary>
public sealed class MetricasOptions
{
    public const string SectionName = "Metricas";

    /// <summary>
    /// Zona horaria del operador. Define qué es "hoy" para el panel.
    ///
    /// No se usa UTC a secas: a las 19:00 en Bogotá, en UTC ya es el día siguiente,
    /// y un pedido de la tarde quedaría fuera del día que el operador está mirando.
    /// </summary>
    public string ZonaHoraria { get; set; } = "America/Bogota";

    /// <summary>
    /// A partir de cuántos minutos una entrega deja de considerarse "a tiempo".
    ///
    /// Es provisional: el pedido todavía no guarda un tiempo prometido (el radio y
    /// el horario del restaurante están fijos en el código del backend), así que
    /// hoy "a tiempo" es un umbral fijo. Cuando exista la promesa real, este valor
    /// desaparece.
    /// </summary>
    public int MinutosParaConsiderarATiempo { get; set; } = 40;

    /// <summary>Cuántas zonas devuelve el desglose, como máximo.</summary>
    public int ZonasMaximas { get; set; } = 8;

    /// <summary>Cuántos eventos devuelve el feed de actividad, como máximo.</summary>
    public int MaximoEventosActividad { get; set; } = 12;
}
