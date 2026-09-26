using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Identity.Service.Services;

/// <summary>
/// Un recurso del catálogo: lo que un servicio expone, la acción de gestión que agrupa sus
/// permisos y las acciones concretas que acepta.
/// </summary>
public sealed record RecursoDelCatalogo(
    string Servicio,
    string Clave,
    string Nombre,
    string Descripcion,
    TipoPermiso Permiso,
    IReadOnlyList<string> Acciones);

/// <summary>
/// El catálogo de recursos que el sistema expone, por servicio, y su siembra en `recursos`.
///
/// Sin este catálogo los permisos no tienen contra qué definirse: `permisos_usuarios` guarda
/// el recurso como texto y ese texto sólo significa algo si está acá. La siembra va por
/// código y no con `HasData` porque es configuración del sistema, no datos de prueba:
/// `HasData` obligaría a una migración por cada recurso nuevo y ensuciaría el historial con
/// cambios que no son de esquema.
///
/// Arranca por los recursos del área admin de Identity, que es lo que esta rebanada
/// protege; los demás servicios agregan los suyos cuando adopten el mecanismo. Es
/// idempotente: corre en cada arranque, actualiza lo que cambió y agrega lo que falta.
/// </summary>
public static class CatalogoRecursos
{
    /// <summary>El servicio dueño de estos recursos.</summary>
    public const string ServicioIdentity = "identity";

    /// <summary>La acción de lectura: la que exige el listado de usuarios.</summary>
    public const string AccionLeer = "READ";

    /// <summary>La acción de edición.</summary>
    public const string AccionEditar = "UPDATE";

    /// <summary>Recurso de las cuentas del portal.</summary>
    public const string ClaveUsuarios = "usuarios";

    /// <summary>Recurso de los niveles de acceso y los permisos que se conceden.</summary>
    public const string ClaveRoles = "roles";

    /// <summary>
    /// Los recursos del área admin de Identity. Es la lista completa de lo que estos
    /// endpoints exponen: un recurso entra acá el día que existe el endpoint que lo exige,
    /// no antes —un recurso que nadie exige no protege nada y hace parecer que el catálogo
    /// cubre más de lo que cubre.
    /// </summary>
    public static readonly IReadOnlyList<RecursoDelCatalogo> Todos =
    [
        new RecursoDelCatalogo(
            ServicioIdentity,
            ClaveUsuarios,
            "Usuarios del portal",
            "Las cuentas del portal que muestra y administra la pantalla de gestión de usuarios.",
            TipoPermiso.GESTION_USUARIOS,
            [AccionLeer, AccionEditar]),
        new RecursoDelCatalogo(
            ServicioIdentity,
            ClaveRoles,
            "Niveles de acceso y permisos",
            "Los presets de nivel de acceso y los permisos por recurso que se conceden a cada usuario.",
            TipoPermiso.GESTION_ROLES,
            [AccionLeer, AccionEditar])
    ];

    /// <summary>
    /// Siembra el catálogo en `recursos`. Los que ya están se actualizan con lo que dice
    /// esta lista —que es la fuente— y los que faltan se agregan. No borra: un recurso que
    /// sale de la lista se desactiva, porque los permisos ya concedidos apuntan a su clave y
    /// borrarlo dejaría filas de permisos hablando de un recurso que no existe.
    /// </summary>
    public static async Task SembrarAsync(IdentityDbContext db, CancellationToken cancellationToken = default)
    {
        var existentes = await db.Recursos.ToListAsync(cancellationToken);

        foreach (var recurso in Todos)
        {
            var acciones = string.Join(",", recurso.Acciones);

            var actual = existentes.FirstOrDefault(r =>
                string.Equals(r.Servicio, recurso.Servicio, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Clave, recurso.Clave, StringComparison.OrdinalIgnoreCase));

            if (actual is null)
            {
                db.Recursos.Add(new Recurso
                {
                    Servicio = recurso.Servicio,
                    Clave = recurso.Clave,
                    Nombre = recurso.Nombre,
                    Descripcion = recurso.Descripcion,
                    Acciones = acciones,
                    Activo = true
                });

                continue;
            }

            actual.Nombre = recurso.Nombre;
            actual.Descripcion = recurso.Descripcion;
            actual.Acciones = acciones;
            actual.Activo = true;
            actual.UpdatedAt = DateTime.UtcNow;
        }

        // Los que ya no están en la lista quedan apagados: siguen existiendo para los
        // permisos que los referencian, pero dejan de ofrecerse.
        var claves = Todos
            .Select(r => r.Clave)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var huerfano in existentes.Where(r =>
            string.Equals(r.Servicio, ServicioIdentity, StringComparison.OrdinalIgnoreCase)
            && r.Activo
            && !claves.Contains(r.Clave)))
        {
            huerfano.Activo = false;
            huerfano.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
