namespace Deuna.Orders.Service.DTOs;

// Contratos de "Servicios finalizados": el historial de pedidos ya cerrados.
//
// Los nombres coinciden con los tipos que usa el portal
// (`src/lib/tipos/servicios-finalizados.ts`): son el acuerdo entre las dos puntas,
// y un cambio acá obliga a un cambio allá. Todavía no hay endpoint ni consulta
// detrás; esto es solo el contrato.
//
// Reglas que valen para todo lo de abajo:
// - Fechas como ISO 8601 (`DateTime` sale así del serializador, nunca como número).
// - Duraciones calculadas acá, en minutos: el cliente no resta fechas. Si el
//   cálculo vive en dos lados, tarde o temprano difieren.
// - Nulabilidad explícita en todo lo que puede faltar, con el motivo al lado.
// - Los estados son los canónicos de Deuna.Shared.Domain.EstadosPedido; no se
//   inventa un literal nuevo para la UI.
// - Los agregados que no se pueden calcular viajan en `null`, no en `0`: un
//   promedio de cero y un promedio que no existe se ven igual en pantalla y
//   significan cosas opuestas.

/// <summary>
/// El sobre de la página.
///
/// <c>Total</c> es el total que cumple los filtros, no la cantidad de <c>Items</c>:
/// con <c>Tamano = 20</c>, la página 2 trae 20 items y un total de, por ejemplo, 137.
/// </summary>
public record PaginaServiciosFinalizados(
    List<ServicioFinalizado> Items,
    int Total,
    int Pagina,
    int Tamano);

/// <summary>
/// Una fila de la tabla: un servicio ya cerrado, con todo lo que la pantalla
/// muestra resuelto por el backend.
/// </summary>
public record ServicioFinalizado(
    Guid PedidoId,
    // Ej: "PED-20260923-000001".
    string Codigo,
    // ISO 8601 — FechaEntrega. Siempre existe si el servicio está cerrado.
    DateTime CerradoEn,
    RestauranteDelServicio Restaurante,
    // null si el pedido se entregó sin asignación registrada.
    DomiciliarioDelServicio? Domiciliario,
    // La sede del restaurante.
    OrigenDelServicio Origen,
    DestinoDelServicio Destino,
    TiemposDelServicio Tiempos,
    ValorDelServicio Valor,
    CalificacionesDelServicio Calificaciones,
    // Hoy siempre { Hay = false, Motivo = null } hasta que exista el modelo de incidencias.
    IncidenciaDelServicio Incidencia,
    // Estado canónico de Deuna.Shared.Domain.EstadosPedido.
    string Estado);

/// <summary>De dónde sale el pedido. Los nombres ya los resolvió Orders con su réplica.</summary>
public record RestauranteDelServicio(
    Guid Id,
    string Nombre,
    string Ciudad);

/// <summary>
/// Quién llevó el pedido.
///
/// Llega por la réplica alimentada por eventos: Orders no le pide datos a Delivery
/// en una lectura.
/// </summary>
public record DomiciliarioDelServicio(
    Guid Id,
    string Nombre,
    // Promedio histórico del domiciliario; null si nunca lo calificaron.
    double? Calificacion);

/// <summary>Punto de recogida: la sede del restaurante.</summary>
public record OrigenDelServicio(
    string Direccion);

/// <summary>
/// Punto de entrega. La zona es la ciudad de la dirección de entrega: es con lo
/// que se filtra la pantalla.
/// </summary>
public record DestinoDelServicio(
    string Direccion,
    string Zona);

/// <summary>
/// Los tiempos del ciclo. Las duraciones vienen calculadas de acá, en minutos:
/// el portal no resta fechas.
/// </summary>
public record TiemposDelServicio(
    // ISO 8601. null si el pedido se cerró sin asignación registrada.
    DateTime? AsignadoEn,
    // ISO 8601. null si el domiciliario nunca escaneó el QR del local.
    DateTime? RecogidoEn,
    // ISO 8601 — siempre existe si el servicio está cerrado.
    DateTime EntregadoEn,
    // null si falta algún extremo del cálculo. No viaja en 0.
    int? MinutosTotales);

/// <summary>Cuánto vale el servicio y quién lo paga.</summary>
public record ValorDelServicio(
    decimal Domicilio,
    decimal Total,
    // "Cliente" o "Domiciliario". null hasta confirmar de dónde sale el dato.
    string? Paga);

/// <summary>
/// Las dos calificaciones del servicio, separadas por sujeto: el promedio del
/// domiciliario no dice lo mismo que el del restaurante.
/// </summary>
public record CalificacionesDelServicio(
    // 1..5. null si el cliente no calificó al domiciliario.
    double? Domiciliario,
    // 1..5. null si el cliente no calificó al restaurante.
    double? Restaurante);

/// <summary>
/// Si el servicio tuvo una incidencia y por qué.
///
/// Hoy siempre <c>{ Hay = false, Motivo = null }</c>: no existe el modelo de
/// incidencias en ningún servicio, así que la columna no puede tener fuente. El
/// campo viaja igual para que la pantalla y el contrato no cambien cuando exista.
/// </summary>
public record IncidenciaDelServicio(
    bool Hay,
    // null cuando no hay incidencia, o cuando todavía no hay modelo que la explique.
    string? Motivo);

/// <summary>
/// Los KPIs de la cabecera.
///
/// Se calculan sobre el mismo conjunto que las filas, no con consultas separadas:
/// así el número de arriba y la lista de abajo no pueden discrepar.
/// </summary>
public record ResumenServiciosFinalizados(
    int CompletadosHoy,
    decimal ValorDomiciliosHoy,
    // null si no hay ninguna calificación en el rango.
    double? CalificacionPromedio,
    // El "basado en N calificaciones" del mockup: sobre cuántas se calculó el promedio.
    int CalificacionesContadas,
    // null mientras no exista el modelo de incidencias. No viaja en 0.
    int? ConIncidencias,
    // null si falta algún extremo del cálculo. No viaja en 0.
    int? TiempoPromedioMin,
    AyerDelResumen Ayer);

/// <summary>
/// El mismo agregado corrido con el rango de ayer, que es lo que necesita la
/// variación porcentual.
/// </summary>
public record AyerDelResumen(
    int Completados,
    decimal ValorDomicilios,
    // null si ayer no se puede calcular: la variación se muestra como "sin dato",
    // no como -100%.
    int? TiempoPromedioMin);

/// <summary>
/// La respuesta del endpoint: el sobre de la página y los KPIs de la cabecera.
///
/// No es un contrato nuevo de datos: compone los dos que ya existen —el sobre y el
/// resumen— sin cambiarles un campo. Viajan juntos porque salen del mismo conjunto: son la
/// misma consulta, no dos que puedan discrepar.
/// </summary>
public record RespuestaServiciosFinalizados(
    PaginaServiciosFinalizados Pagina,
    ResumenServiciosFinalizados Resumen);

/// <summary>
/// Los filtros del historial, tal como llegan por query string. No es parte del contrato de
/// la respuesta: es la forma de la petición.
///
/// Son los filtros que la consulta honra. <c>conIncidencia</c> y <c>pago</c> viajan en la
/// ruta pero no acá: todavía no tienen fuente —no existe el modelo de incidencias y "quién
/// paga" sigue sin decidirse—, así que la petición que los pide se rechaza en vez de
/// devolver un conjunto vacío que se leería como un resultado.
///
/// Todos son opcionales. Sin <c>Desde</c> la ventana arranca hoy a las 00:00 UTC y dura un
/// día, que es lo que hace que <c>CompletadosHoy</c> sea literalmente hoy. <c>Hasta</c> es
/// exclusivo.
/// </summary>
public record FiltroServiciosFinalizados(
    DateTime? Desde,
    DateTime? Hasta,
    Guid? RestauranteId,
    Guid? RepartidorId,
    string? Zona,
    double? CalificacionMin,
    string? Buscar,
    int Pagina,
    int Tamano);
