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
