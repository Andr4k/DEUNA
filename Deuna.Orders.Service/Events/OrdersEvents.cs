namespace Deuna.Orders.Service.Events;

/// <summary>
/// Evento publicado cuando se crea un pedido exitosamente
/// Contrato v1: pedidoId, codigo, clienteId, restauranteId, total, estado, qrCodigo, occurredAt
/// </summary>
public record PedidoCreado(
    Guid PedidoId,
    string Codigo,
    Guid ClienteId,
    Guid RestauranteId,
    decimal Total,
    string Estado,
    string QrCodigo,
    DateTime OccurredAt
);

/// <summary>
/// Evento publicado cuando se actualiza el estado de un pedido
/// </summary>
public record PedidoActualizado(
    Guid PedidoId,
    string Codigo,
    string EstadoAnterior,
    string EstadoNuevo,
    DateTime OccurredAt
);

/// <summary>
/// Evento publicado cuando se cancela un pedido
/// </summary>
public record PedidoCancelado(
    Guid PedidoId,
    string Codigo,
    string Motivo,
    DateTime OccurredAt
);