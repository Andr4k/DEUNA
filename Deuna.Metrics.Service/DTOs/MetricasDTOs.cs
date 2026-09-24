namespace Deuna.Metrics.Service.DTOs;

/// <summary>
/// Contratos del panel.
///
/// Los nombres coinciden con los tipos que ya usa el portal
/// (`src/lib/tipos/metricas.ts`): son el acuerdo entre los dos lados, y un cambio
/// acá obliga a cambiar allá.
/// </summary>
public record IndicadoresDelDia(
    int PedidosTotales,
    int PedidosEnCurso,
    int PedidosEntregados,
    int Incidencias,
    decimal Recaudo,
    VariacionDelDia Variacion);

/// <summary>Cambio contra el día anterior. El portal decide el color.</summary>
public record VariacionDelDia(
    int PedidosTotales,
    int PedidosEnCurso,
    int PedidosEntregados,
    int Incidencias,
    decimal Recaudo);

/// <summary>
/// Cuántos pedidos hay en cada estado del ciclo.
///
/// Solo el estado y el número: la etiqueta que ve el operador la pone el portal.
/// Mandarla desde acá crearía una segunda lista de traducciones que se desincroniza
/// con la del frontend a la primera corrección.
/// </summary>
public record ConteoPorEstado(
    string Estado,
    int Valor);

/// <summary>Estado de la flota de domiciliarios.</summary>
public record ResumenDomiciliarios(
    int Activos,
    int Disponibles,
    int EnServicio,
    int EnDescanso);

/// <summary>
/// Estado de la red de restaurantes.
///
/// `ConPedidosHoy` no puede salir de la base de Identity: lo aporta el dominio de
/// pedidos y lo une el servicio que compone, no una consulta entre bases.
/// </summary>
public record ResumenRestaurantes(
    int Activos,
    int ConPedidosHoy,
    int NuevosHoy);

/// <summary>Todo lo que el panel necesita en una sola llamada.</summary>
public record ResumenPanel(
    IndicadoresDelDia Indicadores,
    IReadOnlyList<ConteoPorEstado> PedidosPorEstado,
    ResumenDomiciliarios Domiciliarios,
    ResumenRestaurantes Restaurantes);

public record Zona(
    string Nombre,
    int Pedidos,
    int Entregados,
    int Pendientes,
    decimal Recaudo);

public record RendimientoDelDia(
    int EntregasATiempo,
    int TiempoPromedioMin,
    double CalificacionPromedio,
    int EntregasMedidas);

public record Actividad(
    string Hora,
    string Tipo,
    string Texto);

/// <summary>
/// Fila interna de un evento, antes de unir el feed.
///
/// Cada subservicio devuelve las suyas con su fecha real, y quien compone las
/// ordena y recorta. Sin la fecha no se pueden intercalar eventos de dominios
/// distintos.
/// </summary>
public record EventoActividad(
    DateTime Fecha,
    string Tipo,
    string Texto);
