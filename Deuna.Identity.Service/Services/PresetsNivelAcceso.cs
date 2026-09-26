using Deuna.Identity.Service.Models.Domain;

namespace Deuna.Identity.Service.Services;

/// <summary>
/// Etiquetas del nivel de acceso que muestra la pantalla de gestión de usuarios.
///
/// "Personalizado" no es un preset que se aplique: es lo que se deriva cuando los
/// permisos del usuario ya no coinciden con ninguno.
/// </summary>
public static class NivelesAcceso
{
    public const string Completo = "Completo";
    public const string Limitado = "Limitado";
    public const string Basico = "Básico";
    public const string Personalizado = "Personalizado";
}

/// <summary>Un preset: el nombre que se muestra y el conjunto exacto de permisos que concede.</summary>
public sealed record PresetNivelAcceso(string Nombre, IReadOnlySet<string> Permisos);

/// <summary>
/// Los presets de nivel de acceso, como CONFIGURACIÓN y no como tabla.
///
/// El nivel de acceso no se guarda en ninguna parte: lo que vale son las filas de
/// permisos_usuarios. El preset es sólo cómo se leen —se elige uno en la pantalla, se
/// escriben sus permisos, y de ahí en adelante la lista de permisos es la verdad—, así
/// que tenerlo en una tabla sería una tercera copia de lo mismo con otro nombre.
///
/// De ahí la derivación: un usuario tiene el nivel del preset cuyos permisos coinciden
/// EXACTAMENTE con los suyos. Un permiso concedido de más o de menos ya no coincide y el
/// nivel pasa a "Personalizado". La coincidencia es exacta a propósito: si el preset se
/// aplicara por subconjunto, un usuario con todos los permisos del sistema se leería
/// como "Básico" por tener los de Básico.
///
/// Sólo cuentan los permisos VIGENTES (concedidos y dentro de su ventana de fechas): un
/// permiso vencido o revocado no es parte de lo que el usuario puede hacer hoy, y dejarlo
/// contar haría que el nivel no cambiara nunca.
/// </summary>
public static class PresetsNivelAcceso
{
    /// <summary>
    /// Los recursos que administra el portal. Espejan los TipoPermiso de gestión y son
    /// la clave del recurso en permisos_usuarios (misma convención que el catálogo `recursos`).
    /// </summary>
    private static readonly string[] RecursosDelPortal =
    [
        "usuarios",
        "roles",
        "restaurantes",
        "repartidores",
        "pedidos",
        "pagos",
        "reportes",
        "configuracion",
        "auditoria"
    ];

    /// <summary>Los recursos de la operación diaria: los que un nivel Limitado puede editar.</summary>
    private static readonly string[] RecursosDeOperacion = ["pedidos", "restaurantes", "repartidores"];

    /// <summary>
    /// Los presets, de mayor a menor. El orden es el de la comparación: si el conjunto del
    /// usuario coincide con más de uno (no puede pasar con estos tres, pero sí al agregar
    /// un preset que sea subconjunto de otro), gana el primero.
    /// </summary>
    public static readonly IReadOnlyList<PresetNivelAcceso> Todos =
    [
        new PresetNivelAcceso(
            NivelesAcceso.Completo,
            Claves(RecursosDelPortal, ["READ", "CREATE", "UPDATE", "DELETE"])),
        new PresetNivelAcceso(
            NivelesAcceso.Limitado,
            // Lectura de todo y edición sólo de la operación.
            Claves(RecursosDelPortal, ["READ"]).Concat(Claves(RecursosDeOperacion, ["UPDATE"])).ToHashSet(StringComparer.OrdinalIgnoreCase)),
        new PresetNivelAcceso(
            NivelesAcceso.Basico,
            Claves(RecursosDelPortal, ["READ"]))
    ];

    /// <summary>Nivel de acceso de un usuario a partir de sus permisos.</summary>
    public static string Derivar(IEnumerable<PermisoUsuario> permisos, DateTime ahora) =>
        DerivarDeClaves(ClavesVigentes(permisos, ahora));

    /// <summary>Nivel que corresponde a un conjunto de claves recurso:acción.</summary>
    public static string DerivarDeClaves(IReadOnlySet<string> claves)
    {
        foreach (var preset in Todos)
        {
            if (preset.Permisos.SetEquals(claves))
            {
                return preset.Nombre;
            }
        }

        return NivelesAcceso.Personalizado;
    }

    /// <summary>
    /// Claves recurso:acción de los permisos vigentes. Un permiso sin recurso o sin acción
    /// no se puede comparar con un preset, así que queda fuera del conjunto (y por lo tanto
    /// el nivel resultante será Personalizado, que es lo honesto: ese permiso existe y no
    /// lo cubre ningún preset).
    /// </summary>
    public static HashSet<string> ClavesVigentes(IEnumerable<PermisoUsuario> permisos, DateTime ahora) =>
        permisos
            .Where(p => p.Concedido
                && !string.IsNullOrWhiteSpace(p.Recurso)
                && !string.IsNullOrWhiteSpace(p.Accion)
                && (p.FechaInicio is null || p.FechaInicio <= ahora)
                && (p.FechaFin is null || p.FechaFin >= ahora))
            .Select(p => Clave(p.Recurso!, p.Accion!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clave de un permiso: recurso en minúsculas y acción en mayúsculas, como los escriben
    /// los DTOs (`"usuarios"`, `"READ"`). Normalizar acá evita que un `"read"` suelto cuente
    /// como un permiso distinto y convierta en Personalizado a un usuario que tiene el preset.
    /// </summary>
    public static string Clave(string recurso, string accion) =>
        $"{recurso.Trim().ToLowerInvariant()}:{accion.Trim().ToUpperInvariant()}";

    private static IReadOnlySet<string> Claves(IEnumerable<string> recursos, IEnumerable<string> acciones) =>
        recursos
            .SelectMany(recurso => acciones.Select(accion => Clave(recurso, accion)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
