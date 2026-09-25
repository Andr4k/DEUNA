namespace Deuna.Orders.Service.Services;

/// <summary>
/// Parámetros del listado de pedidos del panel de administración.
///
/// Viven en configuración y no en el código porque son decisiones de negocio: el mismo
/// criterio que se sigue con el radio de cobertura y el horario del restaurante, que
/// nacieron como constantes y hoy son un pendiente del proyecto.
/// </summary>
public sealed class OrdersOptions
{
    public const string Seccion = "Orders";

    public const string PrioridadAlta = "Alta";
    public const string PrioridadMedia = "Media";
    public const string PrioridadBaja = "Baja";

    /// <summary>
    /// Desde qué valor del domicilio el pedido se considera prioridad Alta.
    ///
    /// PROVISIONAL: los valores los confirma el negocio. Los domicilios observados en la
    /// base rondan los $3.000 a $8.000, así que el corte por defecto cae dentro de esa
    /// familia; el mecanismo (rango de precio en configuración) es lo que importa, no el
    /// número. Se ajusta desde <c>appsettings.json</c> sin recompilar.
    /// </summary>
    public decimal PrioridadAltaDesde { get; set; } = 6000m;

    /// <summary>
    /// Desde qué valor del domicilio el pedido se considera prioridad Media. Por debajo,
    /// Baja. PROVISIONAL, igual que <see cref="PrioridadAltaDesde"/>: lo confirma el negocio.
    /// </summary>
    public decimal PrioridadMediaDesde { get; set; } = 4500m;

    /// <summary>
    /// Minutos de asignación a partir de los cuales se considera que el domiciliario
    /// "no responde".
    ///
    /// Apagado por defecto: <c>0</c> es un valor centinela, no un umbral. El criterio real
    /// de "no responde" todavía no está definido y se cierra cuando exista la app móvil de
    /// domiciliarios; mientras esté en 0, el filtro <c>incluirSinRespuesta</c> NUNCA suma
    /// un pedido en estado Asignado. El filtro se deja listo desde el principio para no
    /// tener que rehacer el endpoint después, pero sin inventar el número.
    /// </summary>
    public int MinutosSinRespuesta { get; set; } = 0;

    /// <summary>
    /// Prioridad derivada del valor del domicilio. A propósito NO depende del tiempo de
    /// espera: es una decisión del dueño del producto.
    /// </summary>
    public string PrioridadPara(decimal valorDomicilio)
    {
        if (valorDomicilio >= PrioridadAltaDesde) return PrioridadAlta;
        if (valorDomicilio >= PrioridadMediaDesde) return PrioridadMedia;
        return PrioridadBaja;
    }
}
