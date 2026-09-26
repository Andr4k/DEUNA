namespace Deuna.Identity.Service.DTOs;

// ========== CONSULTA DEL LISTADO DE USUARIOS ==========
// La forma del pedido de GET /api/v1/admin/identity/usuarios y la del sobre que
// devuelve. Las filas siguen siendo UsuarioPortalResponse (ver UsuariosDTOs.cs): acá
// sólo se agregan los filtros de entrada, los KPIs y la paginación.
//
// El tipo del filtro viaja como TEXTO y no como enum a propósito: un tipo que no existe
// tiene que fallar con 400 (lo valida ConsultaUsuariosValidator). Si el binder
// convirtiera a enum, un `?tipo=MAGO` no bindearía y la consulta saldría SIN filtro,
// devolviendo la lista entera como si el filtro hubiera funcionado.

/// <summary>Pedido del listado, ya con los valores por defecto aplicados.</summary>
public class ConsultaUsuarios
{
    public const int TamanoPaginaPredeterminado = 20;

    /// <summary>Tope de la paginación: más que esto no se puede pedir de una vez.</summary>
    public const int TamanoPaginaMaximo = 100;

    /// <summary>Los tipos de usuario que existen: los tres perfiles del portal.</summary>
    public static readonly IReadOnlyList<string> TiposValidos = Enum.GetNames<TipoUsuarioPortal>();

    /// <summary>Texto libre que se compara contra el nombre y el email. `null` = sin filtro.</summary>
    public string? Busqueda { get; init; }

    /// <summary>Tipo de usuario por el que se filtra. `null` = sin filtro por tipo.</summary>
    public string? Tipo { get; init; }

    public int Pagina { get; init; } = 1;

    public int TamanoPagina { get; init; } = TamanoPaginaPredeterminado;

    /// <summary>
    /// Arma la consulta con los valores por defecto: sin `pagina` ni `tamanoPagina` se
    /// sirve la primera página con el tamaño predeterminado. Un valor inválido NO se
    /// reemplaza por el default —llega tal cual al validador, que lo rechaza—: si el
    /// default tapara el valor pedido, un tope mal pasado pasaría como si estuviera bien.
    /// </summary>
    public static ConsultaUsuarios Crear(string? busqueda, string? tipo, int? pagina, int? tamanoPagina) => new()
    {
        Busqueda = busqueda,
        Tipo = tipo,
        Pagina = pagina ?? 1,
        TamanoPagina = tamanoPagina ?? TamanoPaginaPredeterminado
    };

    /// <summary>
    /// El tipo del filtro como enum, o `null` cuando no se filtró por tipo. Un valor
    /// presente pero que no es un tipo válido lanza: el validador ya lo rechazó, así que
    /// llegar acá con uno inválido es un bug y no un caso a ignorar en silencio.
    /// </summary>
    public TipoUsuarioPortal? TipoDeUsuario() =>
        Tipo is null
            ? null
            : Enum.TryParse<TipoUsuarioPortal>(Tipo.Trim(), ignoreCase: true, out var tipo)
                ? tipo
                : throw new ArgumentException($"Tipo de usuario inválido: {Tipo}", nameof(Tipo));
}

/// <summary>
/// Los KPIs de la pantalla. Salen del MISMO conjunto filtrado que las filas —y antes de
/// paginar—, no de la tabla completa: sobre la tabla, un filtro mostraría dos filas con
/// el total de todas, que es la forma más fácil de que un tablero mienta.
/// </summary>
public record KpisUsuariosResponse(
    int Total,
    int Activos,
    int Inactivos,
    int Eliminados,
    int Administradores,
    int Restaurantes,
    int Repartidores,
    int SinAccesoRegistrado
)
{
    /// <summary>Los KPIs de un conjunto de filas ya filtrado.</summary>
    public static KpisUsuariosResponse De(IReadOnlyList<UsuarioPortalResponse> filas) => new(
        Total: filas.Count,
        Activos: filas.Count(f => f.Estado == EstadoUsuarioPortal.ACTIVO),
        Inactivos: filas.Count(f => f.Estado == EstadoUsuarioPortal.INACTIVO),
        Eliminados: filas.Count(f => f.Estado == EstadoUsuarioPortal.ELIMINADO),
        Administradores: filas.Count(f => f.Tipo == TipoUsuarioPortal.ADMINISTRADOR),
        Restaurantes: filas.Count(f => f.Tipo == TipoUsuarioPortal.RESTAURANTE),
        Repartidores: filas.Count(f => f.Tipo == TipoUsuarioPortal.REPARTIDOR),
        // `null` es el usuario que nunca entró: la pantalla los cuenta aparte.
        SinAccesoRegistrado: filas.Count(f => f.UltimoAcceso is null));
}

/// <summary>La respuesta del listado: la página de filas, los KPIs del filtro y la paginación.</summary>
public record ListadoUsuariosResponse(
    IReadOnlyList<UsuarioPortalResponse> Filas,
    KpisUsuariosResponse Kpis,
    int Pagina,
    int TamanoPagina,
    int TotalPaginas
);
