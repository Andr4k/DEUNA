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
/// Publicado por Orders cuando un pedido se crea exitosamente. Contrato v2.
/// Consumido por Delivery (proyección de pedidos disponibles).
///
/// v2 (TASK-304): el token QR único se separó en los dos que exige FR-002.5. No son
/// intercambiables y pertenecen a partes distintas: el del local lo muestra el restaurante
/// y lo escanea el domiciliario al llegar; el de entrega lo muestra el domiciliario y lo
/// escanea el cliente al recibir.
/// </summary>
public record PedidoCreado(
    Guid PedidoId,
    string Codigo,
    Guid ClienteId,
    Guid RestauranteId,
    decimal Total,
    string Estado,
    string TokenQrLocal,
    string TokenQrEntrega,
    DateTime OccurredAt,
    // Punto de entrega. Aditivo y nullable para no romper mensajes v1 ya encolados:
    // Delivery lo usa como origen del GEOSEARCH de tracking (FR-003.3).
    double? Latitud = null,
    double? Longitud = null
);

/// <summary>
/// Publicado cuando cambia el estado de un pedido. Contrato v2.
///
/// Lo publica **Delivery** con las transiciones del ciclo de entrega:
/// <c>ConfirmadoEnLocal</c> al validar el QR del local, <c>EnRuta</c> al iniciar la entrega y
/// <c>Buscando</c> cuando el domiciliario rechaza antes de llegar al local. Orders lo consume
/// para replicar el estado y no quedarse mostrando el pedido como recién creado; Feedback, para
/// mantener su proyección al día. Orders **no** lo publica: solo cambia el estado como reacción
/// a estos eventos, y republicarlo sería devolverse el propio mensaje.
///
/// v2 (TASK-308): se agregó <see cref="Motivo"/>, opcional y al final para no romper
/// mensajes ya encolados. Es lo que permite que el rechazo quede explicado en el historial
/// del pedido.
/// </summary>
public record PedidoActualizado(
    Guid PedidoId,
    string Codigo,
    string EstadoAnterior,
    string EstadoNuevo,
    DateTime OccurredAt,
    string? Motivo = null
);

/// <summary>
/// Publicado por Delivery cuando el cliente escanea el QR de cierre y la entrega termina
/// (TASK-305). Contrato v1.
///
/// Es el evento terminal del pedido. Consumido por Orders (que cierra el pedido en su
/// propio estado y registra la fecha de entrega) y por Feedback (que solo acepta la
/// encuesta de un pedido <c>Entregado</c>).
/// </summary>
public record PedidoEntregado(
    Guid PedidoId,
    string Codigo,
    Guid RepartidorId,
    DateTime FechaEntrega,
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
