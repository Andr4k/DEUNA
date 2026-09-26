using Deuna.Identity.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Identity.Service.Services;

/// <summary>
/// La siembra del arranque: el catálogo de recursos y el acceso del administrador sembrado.
///
/// El problema que resuelve: el administrador que se creó al desplegar no tiene ninguna fila
/// en `permisos_usuarios`, y la verificación de permisos falla en cerrado. Sin esta siembra
/// ese administrador quedaría afuera de todo, incluidos los endpoints que acaba de habilitar.
///
/// La resolución es explícita: un administrador marcado `EsSuperAdmin` recibe, como filas
/// concretas en `permisos_usuarios`, todas las acciones de todos los recursos del catálogo.
/// No hay bypass por rol ni rama en la que la falta de permisos se lea como permiso: la
/// verificación es la misma para todos y lo que cambia son las filas del administrador.
///
/// Es idempotente: corre en cada arranque y sólo agrega lo que falta.
/// </summary>
public static class SembradoIdentity
{
    public static async Task SembrarAsync(
        IdentityDbContext db,
        IPermisoService permisos,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await CatalogoRecursos.SembrarAsync(db, cancellationToken);

        // `EsSuperAdmin` es la marca del administrador sembrado: es la única que distingue a
        // quien tiene el sistema entero de quien se limita por permisos.
        var superAdmins = await db.Usuarios
            .Include(u => u.PerfilAdministrador)
            .Where(u => u.PerfilAdministrador != null && u.PerfilAdministrador.EsSuperAdmin)
            .ToListAsync(cancellationToken);

        if (superAdmins.Count == 0)
        {
            // Se dice en voz alta: sin super admin no se concede nada automáticamente y la
            // verificación sigue cerrada para todos. Es la diferencia entre no tener
            // permisos y tenerlos todos, y conviene que quede en el log del arranque.
            logger.LogWarning(
                "No hay ningún administrador marcado EsSuperAdmin: no se concedió ningún permiso en esta siembra y la verificación por permiso sigue cerrada para todos");

            return;
        }

        foreach (var admin in superAdmins)
        {
            var concedidos = await permisos.ConcederTodosLosDelCatalogoAsync(admin.Id, cancellationToken);

            if (concedidos > 0)
            {
                logger.LogInformation(
                    "Administrador sembrado {Email} con {Cantidad} permisos del catálogo",
                    admin.Email,
                    concedidos);
            }
        }
    }
}
