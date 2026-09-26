namespace Deuna.Identity.Service.DTOs;

// ========== LISTADO DE USUARIOS (gestión de usuarios) ==========
// Contrato de GET /api/v1/admin/identity/usuarios: lo que la pantalla de gestión
// muestra por fila. El portal lo espeja en src/lib/tipos/usuarios.ts, así que un
// cambio de forma acá obliga a un cambio allá.
//
// Tres campos de la pantalla NO son columnas propias y se resuelven en el listado:
// - Tipo: el perfil que tiene cargado el usuario (uno por tipo, no hay perfil genérico).
// - ZonasAsignadas: sale de las tablas de cobertura que ya existen,
//   zonas_cobertura_repartidor y zonas_cobertura_restaurante. No hay columna que las
//   agregue y no se agrega una: son las filas de esas subtablas.
// - NivelAcceso: es la ETIQUETA del preset aplicado, no la lista de permisos. Lo que
//   vale es lo que quede en permisos_usuarios; el preset es solo cómo se lee.
// - UltimoAcceso: usuarios.LastLoginAt, que AuthService escribe en cada login exitoso.
//   No se agrega una columna nueva ni se deduce de otra tabla.

/// <summary>Tipo de usuario del portal: el perfil que tiene cargado.</summary>
public enum TipoUsuarioPortal
{
    ADMINISTRADOR = 1,
    RESTAURANTE = 2,
    REPARTIDOR = 3
}

/// <summary>
/// Estado de la cuenta, derivado de usuarios.IsActive y usuarios.DeletedAt. Es el
/// estado de la CUENTA, no el del perfil: un perfil apagado con la cuenta viva sigue
/// siendo una cuenta activa, y confundirlos mostraría como inactivo a quien puede entrar.
/// </summary>
public enum EstadoUsuarioPortal
{
    ACTIVO = 1,
    INACTIVO = 2,
    ELIMINADO = 3
}

/// <summary>
/// Una fila de la pantalla de gestión de usuarios.
///
/// <c>UltimoAcceso</c> es `null` cuando el usuario nunca entró (usuarios.LastLoginAt).
/// <c>ZonasAsignadas</c> son las zonas de cobertura VIGENTES del perfil: vacía si el
/// tipo no tiene zonas (un administrador no tiene) y sin las zonas apagadas, que ya no
/// cubren nada.
/// </summary>
public record UsuarioPortalResponse(
    Guid Id,
    string Email,
    string Nombre,
    TipoUsuarioPortal Tipo,
    EstadoUsuarioPortal Estado,
    string NivelAcceso,
    DateTime? UltimoAcceso,
    List<string> ZonasAsignadas,
    DateTime CreatedAt
);
