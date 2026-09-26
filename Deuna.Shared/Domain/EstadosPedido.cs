namespace Deuna.Shared.Domain;

/// <summary>
/// Estados del pedido: la máquina que comparten los servicios.
///
/// Los estados viajan como texto dentro de los eventos, así que esta lista es un contrato
/// tanto como los propios eventos. Si Orders, Delivery y Feedback no escriben exactamente
/// el mismo literal, el pedido parece quedarse quieto o retroceder sin que nada falle: no
/// hay compilador que avise de un string distinto.
/// </summary>
public static class EstadosPedido
{
    /// <summary>El pedido entró al sistema y está buscando domiciliario.</summary>
    public const string Buscando = "Buscando";

    /// <summary>Asignado automáticamente a un domiciliario.</summary>
    public const string Asignado = "Asignado";

    /// <summary>El domiciliario escaneó el QR del local: está físicamente ahí.</summary>
    public const string ConfirmadoEnLocal = "ConfirmadoEnLocal";

    /// <summary>El domiciliario salió del local con el pedido.</summary>
    public const string EnRuta = "EnRuta";

    /// <summary>El cliente escaneó el QR de cierre.</summary>
    public const string Entregado = "Entregado";

    public const string Cancelado = "Cancelado";

    /// <summary>Estados en los que el pedido ya no va a cambiar.</summary>
    public static readonly string[] Terminales = [Entregado, Cancelado];
}
