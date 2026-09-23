namespace Deuna.Shared.Events;

/// <summary>
/// Contratos de integración entre microservicios: fuente única de verdad.
///
/// MassTransit deriva el URN del mensaje y el nombre del exchange desde el
/// namespace + nombre del tipo. Si el publicador y el consumidor usan copias
/// locales en namespaces distintos, se crean DOS exchanges diferentes, la cola
/// del consumidor nunca se vincula al exchange del publicador y el evento se
/// descarta en silencio, sin error ni log.
///
/// Regla: los eventos que cruzan un límite de servicio viven aquí y ambos lados
/// referencian este mismo tipo.
/// </summary>

/// <summary>
/// Publicado por Orders cuando un pedido se crea exitosamente. Contrato v1.
/// Consumido por Delivery (proyección de pedidos disponibles).
/// </summary>
public record PedidoCreado(
    Guid PedidoId,
    string Codigo,
    Guid ClienteId,
    Guid RestauranteId,
    decimal Total,
    string Estado,
    string QrCodigo,
    DateTime OccurredAt,
    // Punto de entrega. Aditivo y nullable para no romper mensajes v1 ya encolados:
    // Delivery lo usa como origen del GEOSEARCH de tracking (FR-003.3).
    double? Latitud = null,
    double? Longitud = null
);

/// <summary>
/// Publicado por Orders cuando cambia el estado de un pedido. Contrato v1.
/// </summary>
public record PedidoActualizado(
    Guid PedidoId,
    string Codigo,
    string EstadoAnterior,
    string EstadoNuevo,
    DateTime OccurredAt
);

/// <summary>
/// Publicado por Orders cuando se cancela un pedido. Contrato v1.
/// </summary>
public record PedidoCancelado(
    Guid PedidoId,
    string Codigo,
    string Motivo,
    DateTime OccurredAt
);

/// <summary>
/// Publicado por Identity cuando se registra un restaurante. Contrato v1.
/// Consumido por Orders para replicar el restaurante y poder validar sus pedidos.
/// </summary>
public record RestauranteRegistrado(
    Guid RestauranteId,
    string NombreComercial,
    string RazonSocial,
    string Nit,
    string DireccionSede,
    string Ciudad,
    double Latitud,
    double Longitud,
    DateTime OccurredAt
);

/// <summary>
/// Publicado por Identity cuando se registra un repartidor. Contrato v1.
/// Consumido por Delivery (matching por cercanía y asignación) y por Feedback
/// (la encuesta califica a un repartidor concreto).
/// </summary>
public record RepartidorRegistrado(
    Guid RepartidorId,
    string NombreCompleto,
    string DocumentoIdentidad,
    string CiudadOperacion,
    string? FotoPerfilUrl,
    DateTime OccurredAt
);

/// <summary>
/// Publicado por Delivery cuando un repartidor acepta un pedido disponible. Contrato v1.
/// </summary>
public record PedidoAsignado(
    Guid PedidoId,
    string Codigo,
    Guid RepartidorId,
    DateTime OccurredAt
);
