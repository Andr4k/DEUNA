using System.Net;
using System.Net.Http.Headers;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Models.Domain;
using Deuna.Identity.Service.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Deuna.Identity.Service.Tests.Usuarios;

/// <summary>
/// La protección por permiso del listado admin de Identity.
///
/// El endpoint ya exigía el rol ADMIN; lo que se prueba acá es la segunda mitad de lo que
/// promete su comentario: un token válido con el rol correcto pero sin la acción concedida
/// sobre el recurso no pasa. El caso que más importa es el 403, porque sin él la verificación
/// sería decorativa.
/// </summary>
public class ProteccionPorPermisoTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Listado = "/api/v1/admin/identity/usuarios";

    private readonly TestWebApplicationFactory _factory;

    public ProteccionPorPermisoTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Listado_ConRolAdminPeroSinElPermiso_Devuelve403()
    {
        var admin = await CrearAdministradorAsync(superAdmin: false);

        using var cliente = ClienteDe(admin);
        var response = await cliente.GetAsync(Listado);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un token válido con rol ADMIN pero sin el permiso concedido no pasa");
    }

    [Fact]
    public async Task Listado_ConElPermisoConcedido_Devuelve200()
    {
        var admin = await CrearAdministradorAsync(superAdmin: false, concederLecturaDeUsuarios: true);

        using var cliente = ClienteDe(admin);
        var response = await cliente.GetAsync(Listado);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Listado_ConElPermisoVencido_Devuelve403()
    {
        var admin = await CrearAdministradorAsync(
            superAdmin: false,
            concederLecturaDeUsuarios: true,
            vencimiento: DateTime.UtcNow.AddHours(-1));

        using var cliente = ClienteDe(admin);
        var response = await cliente.GetAsync(Listado);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un permiso vencido no es parte de lo que el usuario puede hacer hoy");
    }

    /// <summary>
    /// El administrador sembrado no tiene filas de permisos, así que la verificación cerrada
    /// lo deja afuera; la siembra del arranque es la que le concede todo el catálogo. El test
    /// afirma las dos mitades: sin siembra 403, con siembra 200.
    /// </summary>
    [Fact]
    public async Task Listado_DelAdministradorSembrado_SinLaSiembraEs403_YConLaSiembraEs200()
    {
        var sembrado = await CrearAdministradorAsync(superAdmin: true);

        using (var antes = ClienteDe(sembrado))
        {
            var sinSembrar = await antes.GetAsync(Listado);

            sinSembrar.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "la verificación es cerrada: sin filas de permisos el administrador sembrado no pasa");
        }

        await SembrarAsync();

        using var despues = ClienteDe(sembrado);
        var conSiembra = await despues.GetAsync(Listado);

        conSiembra.StatusCode.Should().Be(HttpStatusCode.OK,
            "la siembra le concede al EsSuperAdmin todas las acciones de todos los recursos del catálogo");
    }

    // ---------- helpers ----------

    private HttpClient ClienteDe(Guid usuarioId)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateAdminToken(usuarioId));
        return cliente;
    }

    /// <summary>
    /// Crea un administrador en la base del test. El permiso se escribe como fila concreta:
    /// es lo que la verificación mira, no el rol.
    /// </summary>
    private async Task<Guid> CrearAdministradorAsync(
        bool superAdmin,
        bool concederLecturaDeUsuarios = false,
        DateTime? vencimiento = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var usuario = new Usuario
        {
            Email = $"admin-{Guid.NewGuid():N}@deuna.test",
            PasswordHash = "sin-uso-en-esta-prueba",
            IsActive = true
        };

        db.Usuarios.Add(usuario);
        db.PerfilesAdministrador.Add(new PerfilAdministrador
        {
            UsuarioId = usuario.Id,
            Cargo = "OPERADOR",
            Activo = true,
            EsSuperAdmin = superAdmin
        });

        if (concederLecturaDeUsuarios)
        {
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                UsuarioId = usuario.Id,
                Permiso = TipoPermiso.GESTION_USUARIOS,
                Recurso = CatalogoRecursos.ClaveUsuarios,
                Accion = CatalogoRecursos.AccionLeer,
                Concedido = true,
                FechaFin = vencimiento
            });
        }

        await db.SaveChangesAsync();
        return usuario.Id;
    }

    /// <summary>Corre la siembra del arranque sobre la base del test.</summary>
    private async Task SembrarAsync()
    {
        using var scope = _factory.Services.CreateScope();

        await SembradoIdentity.SembrarAsync(
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPermisoService>(),
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SembradoIdentity).FullName!));
    }
}
