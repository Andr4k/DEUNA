namespace Deuna.Delivery.Service.Events;

/// <summary>
/// Evento consumido cuando Orders publica la creación de un pedido.
/// Contrato v1 (compartido con Deuna.Orders.Service): pedidoId, codigo, clienteId,
/// restauranteId, total, estado, qrCodigo, occurredAt.
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
/// Evento publicado cuando un repartidor acepta un pedido disponible.
/// </summary>
public record PedidoAsignado(
    Guid PedidoId,
    string Codigo,
    Guid RepartidorId,
    DateTime OccurredAt
);
