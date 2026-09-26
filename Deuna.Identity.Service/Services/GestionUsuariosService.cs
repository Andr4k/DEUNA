using Deuna.Identity.Service.DTOs;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deuna.Identity.Service.Services;

/// <summary>Listado de usuarios del portal para la pantalla de gestión de usuarios.</summary>
public interface IGestionUsuariosService
{
    /// <param name="busqueda">Texto libre que se compara contra el nombre y el email.</param>
    /// <param name="tipo">Filtro por tipo de usuario; `null` los trae a todos.</param>
    Task<IReadOnlyList<UsuarioPortalResponse>> ListarAsync(
        string? busqueda,
        TipoUsuarioPortal? tipo,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Arma las filas de la pantalla de gestión de usuarios.
///
/// Los tres datos que no son columnas directas —tipo, zonas y nivel de acceso— se
/// resuelven acá: el tipo y las zonas salen de los perfiles y de sus subtablas de
/// cobertura, y el nivel de acceso se deriva de los permisos del usuario
/// (<see cref="PresetsNivelAcceso"/>). El último acceso es `usuarios.LastLoginAt`, sin
/// unir ninguna tabla.
/// </summary>
public class GestionUsuariosService : IGestionUsuariosService
{
    private readonly IdentityDbContext _db;

    public GestionUsuariosService(IdentityDbContext db) => _db = db;

    public async Task<IReadOnlyList<UsuarioPortalResponse>> ListarAsync(
        string? busqueda,
        TipoUsuarioPortal? tipo,
        CancellationToken cancellationToken = default)
    {
        // Un solo instante para toda la página: si el nivel se derivara con un
        // DateTime.UtcNow por fila, un permiso que vence a mitad de la consulta dejaría
        // a dos usuarios idénticos con niveles distintos.
        var ahora = DateTime.UtcNow;

        var consulta = _db.Usuarios
            .AsNoTracking()
            .Include(u => u.PerfilAdministrador)
            .Include(u => u.PerfilRepartidor).ThenInclude(p => p!.ZonasCobertura)
            .Include(u => u.PerfilRestaurante).ThenInclude(p => p!.ZonasCobertura)
            .Include(u => u.Permisos)
            // El listado es de usuarios DEL PORTAL: quien no tiene ningún perfil no tiene
            // tipo, ni zonas, ni nivel de acceso que mostrar. Sin este filtro entrarían los
            // clientes de la PWA, que no son a quienes apunta esta pantalla.
            .Where(u => u.PerfilAdministrador != null
                || u.PerfilRestaurante != null
                || u.PerfilRepartidor != null);

        if (tipo is not null)
        {
            consulta = tipo switch
            {
                TipoUsuarioPortal.ADMINISTRADOR => consulta.Where(u => u.PerfilAdministrador != null),
                TipoUsuarioPortal.RESTAURANTE => consulta.Where(u => u.PerfilRestaurante != null),
                TipoUsuarioPortal.REPARTIDOR => consulta.Where(u => u.PerfilRepartidor != null),
                _ => consulta
            };
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            // "Nombre o email" incluye el nombre del perfil: el de un repartidor es
            // NombreCompleto y el de un restaurante NombreComercial, que es como se los
            // busca en la pantalla. Se comparan ambos lados en minúsculas, que es lo que
            // traduce a lower(...) LIKE ... en PostgreSQL.
            var patron = busqueda.Trim().ToLowerInvariant();

            consulta = consulta.Where(u =>
                u.Email.ToLower().Contains(patron)
                || (u.FirstName != null && u.FirstName.ToLower().Contains(patron))
                || (u.LastName != null && u.LastName.ToLower().Contains(patron))
                || (u.PerfilRepartidor != null && u.PerfilRepartidor.NombreCompleto.ToLower().Contains(patron))
                || (u.PerfilRestaurante != null && u.PerfilRestaurante.NombreComercial.ToLower().Contains(patron)));
        }

        var usuarios = await consulta
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

        return usuarios.Select(usuario => ARespuesta(usuario, ahora)).ToList();
    }

    private static UsuarioPortalResponse ARespuesta(Usuario usuario, DateTime ahora)
    {
        var tipo = TipoDe(usuario);

        return new UsuarioPortalResponse(
            Id: usuario.Id,
            Email: usuario.Email,
            Nombre: NombreDe(usuario, tipo),
            Tipo: tipo,
            Estado: EstadoDe(usuario),
            NivelAcceso: PresetsNivelAcceso.Derivar(usuario.Permisos, ahora),
            UltimoAcceso: usuario.LastLoginAt,
            ZonasAsignadas: ZonasDe(usuario, tipo),
            CreatedAt: usuario.CreatedAt);
    }

    /// <summary>
    /// El tipo sale del perfil cargado. Las relaciones son 1:1, pero nada impide que un
    /// usuario tenga más de uno: en ese caso manda este orden, que va del más privilegiado
    /// al menos, y queda dicho para que el listado nunca sea ambiguo.
    /// </summary>
    private static TipoUsuarioPortal TipoDe(Usuario usuario) =>
        usuario.PerfilAdministrador is not null ? TipoUsuarioPortal.ADMINISTRADOR
        : usuario.PerfilRestaurante is not null ? TipoUsuarioPortal.RESTAURANTE
        : TipoUsuarioPortal.REPARTIDOR;

    /// <summary>
    /// El estado de la cuenta. Un perfil apagado no la cambia: la cuenta sigue pudiendo
    /// entrar, y mostrarla como inactiva escondería justo al usuario que hay que ir a apagar.
    /// </summary>
    private static EstadoUsuarioPortal EstadoDe(Usuario usuario) =>
        usuario.DeletedAt is not null ? EstadoUsuarioPortal.ELIMINADO
        : !usuario.IsActive ? EstadoUsuarioPortal.INACTIVO
        : EstadoUsuarioPortal.ACTIVO;

    /// <summary>Nombre que muestra la pantalla, según lo que sea el usuario.</summary>
    private static string NombreDe(Usuario usuario, TipoUsuarioPortal tipo)
    {
        var nombre = tipo switch
        {
            TipoUsuarioPortal.RESTAURANTE => usuario.PerfilRestaurante!.NombreComercial,
            TipoUsuarioPortal.REPARTIDOR => usuario.PerfilRepartidor!.NombreCompleto,
            _ => $"{usuario.FirstName} {usuario.LastName}".Trim()
        };

        // Un administrador recién creado puede no tener nombre todavía.
        return string.IsNullOrWhiteSpace(nombre) ? usuario.Email : nombre;
    }

    /// <summary>
    /// Zonas de cobertura del perfil, de las subtablas que ya existen. Sólo las vigentes:
    /// una zona apagada está asignada al perfil pero ya no cubre nada, y el listado dice lo
    /// que el usuario cubre hoy. Los administradores no tienen zonas de cobertura.
    /// </summary>
    private static List<string> ZonasDe(Usuario usuario, TipoUsuarioPortal tipo)
    {
        var zonas = tipo switch
        {
            TipoUsuarioPortal.RESTAURANTE => usuario.PerfilRestaurante!.ZonasCobertura,
            TipoUsuarioPortal.REPARTIDOR => usuario.PerfilRepartidor!.ZonasCobertura,
            _ => []
        };

        return zonas
            .Where(z => z.Activo)
            .Select(z => z.NombreZona)
            .OrderBy(nombre => nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
