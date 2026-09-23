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
}
