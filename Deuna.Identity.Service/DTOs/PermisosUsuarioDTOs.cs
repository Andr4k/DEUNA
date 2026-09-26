using Deuna.Identity.Service.Models.Domain;

namespace Deuna.Identity.Service.DTOs;

// ========== GESTIÓN DE USUARIOS ==========
// Contratos del catálogo de recursos y de los permisos del usuario. Todavía no hay
// endpoint detrás: esto es solo el contrato, con los mismos campos que exponen las
// tablas.
//
// Los permisos se conceden por recurso y acción —"acciones específicas" son los
// accesos a los endpoints—: el endpoint resuelve su recurso + acción y consulta acá
// antes de ejecutar. El nivel de acceso de la pantalla no viaja en estos DTOs porque
// no es un dato guardado: es un preset que aplica un conjunto de permisos, y lo que
// vale es la lista de permisos que quede.

public record RecursoResponse(
    Guid Id,
    string Servicio,
    string Clave,
    string Nombre,
    string? Descripcion,
    string? Acciones,
    bool Activo,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PermisoUsuarioResponse(
    Guid Id,
    Guid UsuarioId,
    TipoPermiso Permiso,
    string? Recurso,
    string? Accion,
    bool Concedido,
    DateTime? FechaInicio,
    DateTime? FechaFin,
    string? Observaciones,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
