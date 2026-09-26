using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Identity.Service.Services;

/// <summary>
/// Resuelve si un usuario tiene concedida una acción concreta sobre un recurso concreto.
///
/// Falla en CERRADO: sin una fila concedida y vigente en `permisos_usuarios` la respuesta
/// es que no. No hay ningún camino en el que la ausencia de permisos cargados se lea como
/// permiso —eso sería fail-open, lo contrario de lo que se pidió—, ni en el que un rol
/// alcance por sí solo. Un usuario sin filas no puede nada, y por eso el acceso del
/// administrador sembrado se resuelve sembrándole sus filas.
/// </summary>
public interface IPermisoService
{
    /// <summary>
    /// Si el usuario tiene la acción concedida sobre el recurso. Recurso y acción se
    /// comparan normalizados —recurso en minúsculas, acción en mayúsculas—, que es la misma
    /// convención con la que se escriben los presets.
    /// </summary>
    Task<bool> TienePermisoAsync(
        Guid usuarioId,
        string recurso,
        string accion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Concede al usuario todas las acciones de todos los recursos del catálogo, como filas
    /// concretas. Es lo que recibe el administrador sembrado: permisos de verdad, no un
    /// bypass —así la verificación sigue siendo la misma para todos—. Idempotente: devuelve
    /// cuántas filas se agregaron o reactivaron.
    /// </summary>
    Task<int> ConcederTodosLosDelCatalogoAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPermisoService"/>
public class PermisoService : IPermisoService
{
    private readonly IdentityDbContext _db;

    public PermisoService(IdentityDbContext db) => _db = db;

    public async Task<bool> TienePermisoAsync(
        Guid usuarioId,
        string recurso,
        string accion,
        CancellationToken cancellationToken = default)
    {
        var recursoNormalizado = NormalizarRecurso(recurso);
        var accionNormalizada = NormalizarAccion(accion);
        var ahora = DateTime.UtcNow;

        return await _db.PermisosUsuario.AnyAsync(p =>
            p.UsuarioId == usuarioId
            && p.Concedido
            && p.Recurso != null
            && p.Recurso.ToLower() == recursoNormalizado
            && p.Accion != null
            && p.Accion.ToUpper() == accionNormalizada
            // Un permiso es vigente dentro de su ventana: uno vencido o todavía no empezado
            // no cuenta, aunque la fila exista.
            && (p.FechaInicio == null || p.FechaInicio <= ahora)
            && (p.FechaFin == null || p.FechaFin >= ahora), cancellationToken);
    }

    public async Task<int> ConcederTodosLosDelCatalogoAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        // El conjunto sale del catálogo en código, no de las filas de `recursos`: los dos se
        // siembran desde la misma lista, así que no pueden divergir, y así la concesión no
        // depende de que la tabla se haya leído bien.
        var vigentes = await _db.PermisosUsuario
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        var concedidos = 0;

        foreach (var recurso in CatalogoRecursos.Todos)
        {
            foreach (var accion in recurso.Acciones)
            {
                var recursoNormalizado = NormalizarRecurso(recurso.Clave);
                var accionNormalizada = NormalizarAccion(accion);

                var existente = vigentes.FirstOrDefault(p =>
                    string.Equals(p.Recurso, recursoNormalizado, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Accion, accionNormalizada, StringComparison.OrdinalIgnoreCase));

                if (existente is null)
                {
                    _db.PermisosUsuario.Add(new PermisoUsuario
                    {
                        UsuarioId = usuarioId,
                        Permiso = recurso.Permiso,
                        Recurso = recursoNormalizado,
                        Accion = accionNormalizada,
                        Concedido = true,
                        Observaciones = "Concedido por la siembra: acceso total del administrador sembrado."
                    });

                    concedidos++;
                }
                else if (!existente.Concedido)
                {
                    // Una fila revocada se reactiva: la siembra dice lo que el administrador
                    // sembrado tiene que tener, y si se revocó fue por error o a mano.
                    existente.Concedido = true;
                    existente.UpdatedAt = DateTime.UtcNow;
                    concedidos++;
                }
            }
        }

        if (concedidos > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return concedidos;
    }

    /// <summary>Recurso en minúsculas, como los escribe el catálogo y los presets.</summary>
    private static string NormalizarRecurso(string recurso) => recurso.Trim().ToLowerInvariant();

    /// <summary>Acción en mayúsculas (READ, UPDATE, ...).</summary>
    private static string NormalizarAccion(string accion) => accion.Trim().ToUpperInvariant();
}
