using Deuna.Identity.Service.Models.Domain;
using Deuna.Identity.Service.Services;
using FluentAssertions;

namespace Deuna.Identity.Service.Tests.Usuarios;

/// <summary>
/// El nivel de acceso es la etiqueta del preset cuyos permisos coinciden con los del
/// usuario, y "Personalizado" cuando no coinciden con ninguno. Lo que se prueba acá es
/// esa derivación, sin base de datos: son permisos en memoria.
/// </summary>
public class PresetsNivelAccesoTests
{
    private static readonly DateTime Ahora = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(NivelesAcceso.Completo)]
    [InlineData(NivelesAcceso.Limitado)]
    [InlineData(NivelesAcceso.Basico)]
    public void Derivar_ConExactamenteLosPermisosDeUnPreset_DevuelveSuEtiqueta(string nombreDelPreset)
    {
        var preset = PresetsNivelAcceso.Todos.Single(p => p.Nombre == nombreDelPreset);
        var permisos = preset.Permisos.Select(clave => APermiso(clave)).ToList();

        var nivel = PresetsNivelAcceso.Derivar(permisos, Ahora);

        nivel.Should().Be(nombreDelPreset);
    }

    [Fact]
    public void Derivar_ConUnPermisoDeMas_DevuelvePersonalizado()
    {
        var permisos = PermisosDe(NivelesAcceso.Basico)
            .Append(APermiso("pedidos:CREATE"))
            .ToList();

        var nivel = PresetsNivelAcceso.Derivar(permisos, Ahora);

        nivel.Should().Be(NivelesAcceso.Personalizado);
    }

    [Fact]
    public void Derivar_ConUnPermisoDeMenos_DevuelvePersonalizado()
    {
        // Todos los de Básico salvo uno: es el caso que un "contiene a" daría por bueno.
        var permisos = PermisosDe(NivelesAcceso.Basico).Skip(1).ToList();

        var nivel = PresetsNivelAcceso.Derivar(permisos, Ahora);

        nivel.Should().Be(NivelesAcceso.Personalizado);
    }

    [Fact]
    public void Derivar_IgnoraLosPermisosNoConcedidosYLosFueraDeVigencia()
    {
        var permisos = PermisosDe(NivelesAcceso.Basico).ToList();
        permisos.Add(APermiso("pedidos:CREATE", concedido: false));
        permisos.Add(APermiso("pedidos:UPDATE", fechaFin: Ahora.AddDays(-1)));
        permisos.Add(APermiso("pedidos:DELETE", fechaInicio: Ahora.AddDays(1)));

        var nivel = PresetsNivelAcceso.Derivar(permisos, Ahora);

        nivel.Should().Be(NivelesAcceso.Basico);
    }

    [Fact]
    public void Derivar_SinPermisosVigentes_DevuelvePersonalizado()
    {
        var nivel = PresetsNivelAcceso.Derivar([], Ahora);

        nivel.Should().Be(NivelesAcceso.Personalizado);
    }

    [Fact]
    public void Derivar_NormalizaElRecursoYLaAccion_ReconoceElPreset()
    {
        // El mismo permiso escrito con otras mayúsculas y con espacios de más.
        var permisos = PermisosDe(NivelesAcceso.Basico)
            .Select(p => new PermisoUsuario
            {
                Recurso = $" {p.Recurso!.ToUpperInvariant()}",
                Accion = p.Accion!.ToLowerInvariant(),
                Concedido = true
            })
            .ToList();

        var nivel = PresetsNivelAcceso.Derivar(permisos, Ahora);

        nivel.Should().Be(NivelesAcceso.Basico);
    }

    private static IEnumerable<PermisoUsuario> PermisosDe(string nombreDelPreset) =>
        PresetsNivelAcceso.Todos.Single(p => p.Nombre == nombreDelPreset).Permisos.Select(clave => APermiso(clave));

    private static PermisoUsuario APermiso(
        string clave,
        bool concedido = true,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null)
    {
        var partes = clave.Split(':');

        return new PermisoUsuario
        {
            Permiso = TipoPermiso.GESTION_USUARIOS,
            Recurso = partes[0],
            Accion = partes[1],
            Concedido = concedido,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin
        };
    }
}
