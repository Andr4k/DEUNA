using Deuna.Metrics.Service.DTOs;
using Deuna.Metrics.Service.Models;
using Deuna.Metrics.Service.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Deuna.Metrics.Service.Tests.Services;

/// <summary>
/// Tests del compositor.
///
/// Lo que se prueba acá es la lógica propia de este servicio: qué pasa cuando se
/// juntan datos de dominios distintos. Las consultas SQL no se prueban con mocks
/// (un mock de SQL prueba que el mock devuelve lo que se le dijo): se verifican
/// contra las bases reales con el script end-to-end.
/// </summary>
public class MetricasPanelTests
{
    private readonly Mock<IMetricasPedidos> _pedidos = new();
    private readonly Mock<IMetricasDomiciliarios> _domiciliarios = new();
    private readonly Mock<IMetricasFeedback> _feedback = new();
    private readonly Mock<IMetricasRestaurantes> _restaurantes = new();

    private MetricasPanel Componer(int maximoEventos = 12) => new(
        _pedidos.Object,
        _domiciliarios.Object,
        _feedback.Object,
        _restaurantes.Object,
        new RelojDelPanel(Options.Create(new MetricasOptions
        {
            ZonaHoraria = "America/Bogota",
            MaximoEventosActividad = maximoEventos,
        })),
        Options.Create(new MetricasOptions { MaximoEventosActividad = maximoEventos }));

    private static IndicadoresDelDia Indicadores(int total, int curso, int entregados, int incidencias, decimal recaudo) =>
        new(total, curso, entregados, incidencias, recaudo, new VariacionDelDia(0, 0, 0, 0, 0));

    private void Sembrar(IndicadoresDelDia hoy, IndicadoresDelDia ayer)
    {
        _pedidos
            .Setup(p => p.IndicadoresAsync(It.IsAny<RangoTiempo>(), It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hoy, ayer));

        _pedidos
            .Setup(p => p.PorEstadoAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConteoPorEstado> { new("Buscando", 3) });

        _domiciliarios
            .Setup(d => d.ResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenDomiciliarios(10, 6, 3, 1));

        _restaurantes
            .Setup(r => r.ResumenAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenRestaurantes(Activos: 20, ConPedidosHoy: 0, NuevosHoy: 2));
    }

    [Fact]
    public async Task PanelAsync_LaVariacionEsLaDiferenciaContraAyer()
    {
        Sembrar(
            hoy: Indicadores(120, 30, 80, 4, 150_000m),
            ayer: Indicadores(100, 25, 70, 6, 120_000m));

        var panel = await Componer().PanelAsync(CancellationToken.None);

        panel.Indicadores.Variacion.Should().BeEquivalentTo(new VariacionDelDia(
            PedidosTotales: 20,
            PedidosEnCurso: 5,
            PedidosEntregados: 10,
            Incidencias: -2,
            Recaudo: 30_000m));
    }

    /// <summary>
    /// El dato más delicado del panel: cuántos restaurantes recibieron pedidos hoy
    /// vive en la base de Orders, pero la tarjeta es de restaurantes. Si el
    /// compositor no une los dos dominios, la tarjeta muestra 0 para siempre —
    /// y un cero no llama la atención, así que nadie lo reporta.
    /// </summary>
    [Fact]
    public async Task PanelAsync_LosRestaurantesConPedidosLosAportaPedidosNoIdentity()
    {
        Sembrar(
            hoy: Indicadores(10, 2, 8, 0, 12_000m),
            ayer: Indicadores(10, 2, 8, 0, 12_000m));

        _pedidos
            .Setup(p => p.RestaurantesConPedidosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var panel = await Componer().PanelAsync(CancellationToken.None);

        panel.Restaurantes.ConPedidosHoy.Should().Be(7);
        // Y los otros dos siguen viniendo de Identity.
        panel.Restaurantes.Activos.Should().Be(20);
        panel.Restaurantes.NuevosHoy.Should().Be(2);
    }

    [Fact]
    public async Task PanelAsync_ElPanelTraeLasCuatroTarjetas()
    {
        Sembrar(
            hoy: Indicadores(10, 2, 8, 0, 12_000m),
            ayer: Indicadores(8, 1, 7, 1, 10_000m));

        _pedidos
            .Setup(p => p.RestaurantesConPedidosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var panel = await Componer().PanelAsync(CancellationToken.None);

        panel.Indicadores.PedidosTotales.Should().Be(10);
        panel.PedidosPorEstado.Should().ContainSingle(c => c.Estado == "Buscando" && c.Valor == 3);
        panel.Domiciliarios.EnServicio.Should().Be(3);
        panel.Restaurantes.Activos.Should().Be(20);
    }

    [Fact]
    public async Task RendimientoAsync_CombinaTiemposDeDomiciliariosConCalificacionDeFeedback()
    {
        _domiciliarios
            .Setup(d => d.TiemposEntregaAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ATiempo: 18, Medidas: 20, PromedioMin: 27));

        _feedback
            .Setup(f => f.CalificacionAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Promedio: 4.83, Encuestas: 12));

        var rendimiento = await Componer().RendimientoAsync(CancellationToken.None);

        rendimiento.EntregasATiempo.Should().Be(18);
        rendimiento.TiempoPromedioMin.Should().Be(27);
        rendimiento.CalificacionPromedio.Should().Be(4.8, "se redondea a un decimal para la estrella");
        rendimiento.EntregasMedidas.Should().Be(20, "el panel necesita saber sobre cuántas se calculó");
    }

    [Fact]
    public async Task RendimientoAsync_SinEncuestasLaCalificacionEsCeroYNoRompe()
    {
        _domiciliarios
            .Setup(d => d.TiemposEntregaAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ATiempo: 0, Medidas: 0, PromedioMin: 0));

        _feedback
            .Setup(f => f.CalificacionAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Promedio: 0d, Encuestas: 0));

        var rendimiento = await Componer().RendimientoAsync(CancellationToken.None);

        rendimiento.CalificacionPromedio.Should().Be(0);
        rendimiento.EntregasMedidas.Should().Be(0);
    }

    /// <summary>
    /// El feed mezcla dominios. Si cada subservicio recortara antes, el panel
    /// mostraría los últimos de cada uno en lugar de los últimos de la red: se
    /// verían entregas viejas mientras los pedidos nuevos quedan afuera.
    /// </summary>
    [Fact]
    public async Task ActividadAsync_MezclaLosTresDominiosPorFechaYRecorta()
    {
        var baseFecha = new DateTime(2026, 9, 24, 18, 0, 0, DateTimeKind.Utc);

        _pedidos
            .Setup(p => p.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad>
            {
                new(baseFecha.AddMinutes(-1), "pedido", "pedido nuevo"),
                new(baseFecha.AddMinutes(-10), "pedido", "pedido viejo"),
            });

        _domiciliarios
            .Setup(d => d.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad>
            {
                new(baseFecha, "entrega", "entrega reciente"),
            });

        _feedback
            .Setup(f => f.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad>
            {
                new(baseFecha.AddMinutes(-5), "servicio", "calificación"),
            });

        var actividad = await Componer(maximoEventos: 3).ActividadAsync(CancellationToken.None);

        actividad.Should().HaveCount(3, "se recorta al máximo configurado");
        actividad.Select(a => a.Texto).Should().ContainInOrder(
            "entrega reciente", "pedido nuevo", "calificación");
        actividad.Select(a => a.Tipo).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ActividadAsync_DevuelveElTipoDeCadaDominioParaQueElPortalColoree()
    {
        var fecha = new DateTime(2026, 9, 24, 18, 0, 0, DateTimeKind.Utc);

        _pedidos.Setup(p => p.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad> { new(fecha, "pedido", "uno") });
        _domiciliarios.Setup(d => d.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad>());
        _feedback.Setup(f => f.EventosAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EventoActividad>());

        var actividad = await Componer().ActividadAsync(CancellationToken.None);

        // El tipo viaja crudo: el portal decide el color y el texto de cada uno.
        actividad.Single().Tipo.Should().Be("pedido");
        actividad.Single().Hora.Should().MatchRegex(@"^\d{2}:\d{2}$");
    }

    [Fact]
    public async Task ZonasAsync_DelegaEnElDominioDePedidos()
    {
        _pedidos
            .Setup(p => p.ZonasAsync(It.IsAny<RangoTiempo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Zona> { new("Bogota", 60, 50, 10, 240_000m) });

        var zonas = await Componer().ZonasAsync(CancellationToken.None);

        zonas.Should().ContainSingle(z => z.Nombre == "Bogota" && z.Pedidos == 60);
    }
}
