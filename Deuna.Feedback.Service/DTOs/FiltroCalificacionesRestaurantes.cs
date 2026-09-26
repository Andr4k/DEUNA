namespace Deuna.Feedback.Service.DTOs;

/// <summary>
/// Los filtros que honran la lista y el resumen de calificaciones a restaurantes.
///
/// Son los mismos para las dos operaciones a propósito (sección 4.2 del plan): la lista y
/// los KPIs salen del mismo conjunto filtrado, así que el número de la cabecera y la tabla
/// de abajo no pueden discrepar. Si cada uno armara su propia consulta, dos WHERE parecidos
/// terminarían divergiendo.
///
/// <c>Desde</c> es obligatorio: sin ventana no hay pantalla que mostrar. <c>Hasta</c> es
/// exclusivo, con el mismo criterio que la consulta de la sección 4.1
/// (<c>CreatedAt &gt;= @desde and CreatedAt &lt; @hasta</c>); sin él la ventana queda
/// abierta hacia adelante.
///
/// No hay filtro por tipo de comida ni por incidencias: no existe la fuente de ninguno de
/// los dos. Un filtro que se acepta y se ignora devuelve el conjunto entero y se lee como si
/// hubiera filtrado.
/// </summary>
public record FiltroCalificacionesRestaurantes(
    DateTime Desde,
    DateTime? Hasta = null,
    string? Buscar = null,
    string? Zona = null,
    double? CalificacionMin = null,
    double? CalificacionMax = null,
    int Pagina = 1,
    int Tamano = 20);
