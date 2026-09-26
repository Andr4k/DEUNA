using Deuna.Feedback.Service.DTOs;

namespace Deuna.Feedback.Service.Services;

/// <summary>
/// La lectura de la pantalla de calificaciones a restaurantes (secciones 4.1 y 4.2 del plan).
///
/// Las dos operaciones son endpoints distintos, pero salen del MISMO conjunto filtrado: la
/// lista y el resumen comparten los filtros y la ventana, así que el KPI de la cabecera y la
/// tabla de abajo no pueden contar cosas distintas.
/// </summary>
public interface ICalificacionesRestaurantesService
{
    /// <summary>
    /// El agregado por restaurante del rango (sección 4.1 del plan): promedio, total,
    /// distribución en conteos, aspectos agrupados por el criterio tal como llegó, nombre y
    /// zona de la réplica, y tendencia contra el período anterior.
    /// </summary>
    Task<PaginaCalificacionesRestaurantes> ObtenerCalificacionesAsync(
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Los KPIs de la cabecera, calculados sobre el resultado ya filtrado —no con consultas
    /// aparte— para que no puedan discrepar de las filas.
    /// </summary>
    Task<ResumenCalificacionesRestaurantes> ObtenerResumenAsync(
        FiltroCalificacionesRestaurantes filtro,
        CancellationToken cancellationToken = default);
}
