using System.ComponentModel.DataAnnotations;

namespace Deuna.Orders.Service.DTOs;

/// <summary>
/// Request para crear un pedido
/// </summary>
public record CrearPedidoRequest(
    [Required] Guid RestauranteId,
    [Required] List<CrearItemPedidoRequest> Items,
    [Required] CrearDireccionEntregaRequest DireccionEntrega,
    [Required] List<CrearContactoPedidoRequest> Contactos,
    string? NotasCliente = null
);

/// <summary>
/// Item del pedido en el request
/// </summary>
public record CrearItemPedidoRequest(
    [Required] [MaxLength(200)] string NombreProducto,
    [MaxLength(500)] string? Descripcion,
    [Required] [Range(1, 100)] int Cantidad,
    [Required] [Range(0.01, 999999.99)] decimal PrecioUnitario
);

/// <summary>
/// Dirección de entrega en el request
/// </summary>
public record CrearDireccionEntregaRequest(
    [Required] [MaxLength(200)] string Calle,
    [MaxLength(100)] string? Numero,
    [MaxLength(100)] string? Interior,
    [MaxLength(100)] string? Referencia,
    [Required] [MaxLength(100)] string Ciudad,
    [MaxLength(100)] string? Departamento,
    [MaxLength(20)] string? CodigoPostal,
    [Required] decimal Latitud,
    [Required] decimal Longitud
);

/// <summary>
/// Contacto del pedido en el request
/// </summary>
public record CrearContactoPedidoRequest(
    [Required] [MaxLength(50)] string Tipo, // Cliente, Restaurante, Repartidor
    [Required] [MaxLength(100)] string Nombre,
    [Required] [MaxLength(20)] string Telefono,
    [MaxLength(256)] string? Email
);

/// <summary>
/// Response al crear un pedido
/// </summary>
public record CrearPedidoResponse(
    Guid PedidoId,
    string Codigo,
    /// <summary>Token que el restaurante muestra en su app para que el domiciliario lo escanee.</summary>
    string TokenQrLocal,
    string Estado,
    decimal Subtotal,
    decimal CostoEnvio,
    decimal Total,
    DateTime FechaCreacion
);

/// <summary>
/// Cancelación de un pedido desde el restaurante. El pedido va en la ruta; acá solo viaja
/// el motivo, que es lo que el cliente y el domiciliario necesitan saber.
/// </summary>
public record CancelarPedidoRequest(
    [Required, MaxLength(300)] string Motivo
);

/// <summary>
/// Response completo de un pedido
/// </summary>
public record PedidoResponse(
    Guid Id,
    Guid ClienteId,
    Guid RestauranteId,
    string Codigo,
    string Estado,
    decimal Subtotal,
    decimal CostoEnvio,
    decimal Total,
    DireccionEntregaResponse DireccionEntrega,
    /// <summary>
    /// Solo el token del local: es el que el restaurante muestra. El token de entrega lo
    /// muestra el domiciliario, y le llega por Delivery (GET /my-orders), no por acá.
    /// </summary>
    string TokenQrLocal,
    string? NotasCliente,
    string? NotasRestaurante,
    DateTime FechaCreacion,
    DateTime? FechaConfirmacion,
    DateTime? FechaPreparacion,
    DateTime? FechaListo,
    DateTime? FechaEntrega,
    DateTime? FechaCancelacion,
    List<ItemPedidoResponse> Items,
    List<ContactoPedidoResponse> Contactos,
    TarifaAplicadaResponse? Tarifa
);

/// <summary>
/// Dirección de entrega en response
/// </summary>
public record DireccionEntregaResponse(
    Guid Id,
    string Calle,
    string? Numero,
    string? Interior,
    string? Referencia,
    string Ciudad,
    string? Departamento,
    string? CodigoPostal,
    decimal Latitud,
    decimal Longitud
);

/// <summary>
/// Item del pedido en response
/// </summary>
public record ItemPedidoResponse(
    Guid Id,
    string NombreProducto,
    string? Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Subtotal
);

/// <summary>
/// Contacto del pedido en response
/// </summary>
public record ContactoPedidoResponse(
    Guid Id,
    string Tipo,
    string Nombre,
    string Telefono,
    string? Email
);

/// <summary>
/// Tarifa aplicada en response
/// </summary>
public record TarifaAplicadaResponse(
    Guid Id,
    string TipoTarifa,
    decimal CostoBase,
    decimal? CostoPorKm,
    decimal? DistanciaKm,
    decimal? CostoAdicional,
    string? DetalleCalculo,
    decimal TotalCalculado
);

/// <summary>
/// Filtros del listado de pedidos sin asignar. Llegan por query string; todos son
/// opcionales salvo la paginación, que tiene valores por defecto.
/// </summary>
public record FiltroPedidosSinAsignar(
    string? Zona,
    string? Prioridad,
    int? EsperaMin,
    bool IncluirSinRespuesta,
    int Pagina,
    int Tamano);

/// <summary>
/// Un pedido sin asignar, tal como lo muestra la lista del administrador.
/// El nombre del domiciliario no está acá: Orders no tiene la réplica de repartidores
/// (viven en Delivery), así que en la lista se muestra el pedido y su antigüedad.
/// </summary>
public record PedidoSinAsignarItem(
    Guid PedidoId,
    string Codigo,
    string Restaurante,
    string RecogerEn,
    string EntregarEn,
    DateTime GeneradoEn,
    int MinutosEsperando,
    string Prioridad,
    decimal ValorDomicilio,
    string Zona,
    /// <summary>
    /// Estado del pedido. Hoy siempre es <c>Buscando</c>, pero cuando el umbral de
    /// "no responde" se encienda la lista va a mezclar los que nunca se asignaron con los
    /// que quedaron en manos de alguien que no contesta, y el operador tiene que poder
    /// distinguirlos para saber qué está resolviendo.
    /// </summary>
    string Estado);

/// <summary>
/// Página del listado: los items de la página y el total completo de la lista (no el de
/// la página), que es lo que necesita el portal para saber cuántas páginas hay.
/// </summary>
public record ListaPedidosSinAsignarResponse(
    List<PedidoSinAsignarItem> Items,
    int Total,
    int Pagina,
    int Tamano);