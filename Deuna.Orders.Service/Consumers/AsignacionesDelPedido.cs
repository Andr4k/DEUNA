using Deuna.Orders.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Orders.Service.Consumers;

/// <summary>
/// Consultas del historial de asignaciones que comparten los consumers del ciclo de entrega.
/// </summary>
internal static class AsignacionesDelPedido
{
    /// <summary>
    /// Intento vigente del pedido: el último que no fue rechazado. Después de entregar queda
    /// en <c>Completado</c>, que sigue siendo el intento vigente.
    /// </summary>
    public static Task<AsignacionReplicada?> VigenteAsync(
        OrdersDbContext db,
        Guid pedidoId,
        CancellationToken cancellationToken) =>
        db.AsignacionesReplicadas
            .Where(a => a.PedidoId == pedidoId
                        && a.EstadoAsignacion != AsignacionReplicada.EstadoRechazado)
            .OrderByDescending(a => a.FechaAsignacion)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// El intento vigente de un pedido ya cargado en memoria, con la misma regla que
    /// <see cref="VigenteAsync"/>: el último que no fue rechazado.
    ///
    /// Existe porque la consulta del historial de servicios finalizados resuelve ese cruce en
    /// memoria —la asignación es 1:N y elegir el intento es una regla, no un join— y la regla
    /// no puede vivir en dos versiones: la de SQL y la de acá tienen que decir lo mismo.
    /// </summary>
    public static AsignacionReplicada? Vigente(IEnumerable<AsignacionReplicada> asignaciones, Guid pedidoId) =>
        asignaciones
            .Where(a => a.PedidoId == pedidoId
                        && a.EstadoAsignacion != AsignacionReplicada.EstadoRechazado)
            .OrderByDescending(a => a.FechaAsignacion)
            .FirstOrDefault();
}
