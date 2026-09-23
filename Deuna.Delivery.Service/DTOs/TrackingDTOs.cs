using System.ComponentModel.DataAnnotations;

namespace Deuna.Delivery.Service.DTOs;

/// <summary>
/// Telemetría GPS que envía la app móvil del repartidor (US-003.3).
/// </summary>
public record ActualizarUbicacionRequest(
    [Required] Guid RiderId,
    [Required] double Latitude,
    [Required] double Longitude,
    [Required] DateTime Timestamp
);

/// <summary>
/// Ubicación conocida del repartidor asociado a un pedido.
/// </summary>
public record UbicacionRepartidorResponse(
    Guid OrderId,
    Guid RiderId,
    double Latitude,
    double Longitude,
    double? DistanciaMetros,
    DateTime? ActualizadoEn
);

/// <summary>
/// Rechazo del pedido asignado (TASK-303). El motivo es opcional: obligarlo llevaría a
/// motivos inventados, y el dato que importa es que el pedido vuelva a búsqueda.
/// </summary>
public record RechazarPedidoRequest(
    [MaxLength(300)] string? Motivo
);

/// <summary>
/// Validación del QR de recogida en el local (TASK-304). El domiciliario escanea el QR que
/// muestra el restaurante y lo manda acá: es la prueba de que llegó físicamente.
/// </summary>
public record ValidarQrLocalRequest(
    [Required] Guid OrderId,
    [Required, MaxLength(64)] string TokenQrLocal
);
