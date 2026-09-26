using Deuna.Delivery.Service.Models;
using Deuna.Delivery.Service.Services;
using Deuna.Delivery.Service.Tests.Tracking;
using Deuna.Shared.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Deuna.Delivery.Service.Tests.Assignment;

/// <summary>
/// TASK-305: inicio de la entrega y cierre por QR.
///
/// El domiciliario inicia la entrega desde el local (EnRuta) y el cliente la cierra
/// escaneando el QR que el domiciliario le muestra. El cierre es público: quien escanea no
/// tiene sesión, así que el token es lo único que autoriza.
///
/// Redis real (TestContainers): FR-003.10 pide limpiar la telemetría del domiciliario al
/// cerrar, y eso no se puede verificar contra un doble en memoria.
/// </summary>
[Collection(RedisCollection.Name)]
public class CicloEntregaTests : IDisposable
{
    private static readonly Guid RepartidorId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid OtroRepartidorId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private const double Lat = 4.6097100;
    private const double Lon = -74.0817500;
    private const string TokenEntrega = "ffffffffffffffffffffffffffffffff";
    private const string TokenLocal = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";

    private readonly IConnectionMultiplexer _redis;
    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _store;
    private readonly Mock<IPublishEndpoint> _publish = new();

    public CicloEntregaTests(RedisContainerFixture redis)
    {
        _redis = redis.CrearConexion();
        _db = new DeliveryDbContext(new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase($"ciclo-entrega-{Guid.NewGuid()}")
            .Options);
        _store = new RedisTrackingStore(_redis, NullLogger<RedisTrackingStore>.Instance);

        LimpiarTelemetria();
    }

    public void Dispose()
    {
        LimpiarTelemetria();
        _db.Dispose();
        _redis.Dispose();
    }

    private void LimpiarTelemetria()
    {
        var db = _redis.GetDatabase();
        db.KeyDelete(RedisTrackingStore.ClaveRepartidores);
        db.KeyDelete(RedisTrackingStore.ClaveTimestamps);
    }

    private IAsignacionService CrearServicio() =>
        new AsignacionService(
            _db,
            _store,
            _publish.Object,
            Options.Create(new AsignacionOptions()),
            NullLogger<AsignacionService>.Instance);

    /// <summary>Pedido en el estado indicado, con su asignación activa.</summary>
    private async Task<PedidoDisponible> CrearPedidoAsync(string estado, Guid? repartidorId = null)
    {
        var pedido = new PedidoDisponible
        {
            PedidoId = Guid.NewGuid(),
            Codigo = $"PED-{Guid.NewGuid().ToString("N")[..6]}",
            ClienteId = Guid.NewGuid(),
            RestauranteId = Guid.NewGuid(),
            Total = 40000m,
            Estado = estado,
            TokenQrLocal = TokenLocal,
            TokenQrEntrega = TokenEntrega,
            Latitud = Lat,
            Longitud = Lon,
            RepartidorId = repartidorId,
            AsignadoAt = repartidorId is null ? null : DateTime.UtcNow
        };

        _db.PedidosDisponibles.Add(pedido);

        if (repartidorId is not null)
        {
            _db.AsignacionesRepartidor.Add(new AsignacionRepartidor
            {
                PedidoId = pedido.PedidoId,
                RepartidorId = repartidorId.Value,
                Estado = AsignacionRepartidor.EstadoAsignado,
                DistanciaMetros = 300
            });
        }

        await _db.SaveChangesAsync();
        return pedido;
    }

    // ------------------------------------------------------------- inicio de entrega

    [Fact]
    public async Task IniciarEntrega_PoneElPedidoEnRutaYPublicaElCambio()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoConfirmadoEnLocal, RepartidorId);

        var resultado = await CrearServicio().IniciarEntregaAsync(pedido.PedidoId, RepartidorId);

        resultado.Iniciada.Should().BeTrue();
        resultado.Estado.Should().Be(PedidoDisponible.EstadoEnRuta);

        var guardado = await _db.PedidosDisponibles.SingleAsync();
        guardado.Estado.Should().Be(PedidoDisponible.EstadoEnRuta);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync();
        asignacion.FechaInicioEntrega.Should().NotBeNull();

        _publish.Verify(p => p.Publish(
            It.Is<PedidoActualizado>(e =>
                e.PedidoId == pedido.PedidoId &&
                e.EstadoAnterior == PedidoDisponible.EstadoConfirmadoEnLocal &&
                e.EstadoNuevo == PedidoDisponible.EstadoEnRuta),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IniciarEntrega_SinHaberConfirmadoElLocal_SeRechaza()
    {
        // El pedido está asignado pero el domiciliario todavía no escaneó el QR del local:
        // ponerse en ruta sería salir sin haber recogido el pedido.
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RepartidorId);

        var resultado = await CrearServicio().IniciarEntregaAsync(pedido.PedidoId, RepartidorId);

        resultado.Iniciada.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.EstadoInvalido);

        var guardado = await _db.PedidosDisponibles.SingleAsync();
        guardado.Estado.Should().Be(PedidoDisponible.EstadoAsignado);
    }

    [Fact]
    public async Task IniciarEntrega_OtroDomiciliario_SeRechaza()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoConfirmadoEnLocal, RepartidorId);

        var resultado = await CrearServicio().IniciarEntregaAsync(pedido.PedidoId, OtroRepartidorId);

        resultado.Iniciada.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.NoAsignado);
    }

    [Fact]
    public async Task IniciarEntrega_DosVeces_SeRechazaLaSegunda()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoConfirmadoEnLocal, RepartidorId);
        var servicio = CrearServicio();

        await servicio.IniciarEntregaAsync(pedido.PedidoId, RepartidorId);
        var segunda = await servicio.IniciarEntregaAsync(pedido.PedidoId, RepartidorId);

        segunda.Iniciada.Should().BeFalse();
        segunda.Motivo.Should().Be(MotivoRechazo.EstadoInvalido);
    }

    // --------------------------------------------------------------- cierre por QR

    [Fact]
    public async Task RechazarSinOtroDomiciliario_RegistraElRechazoIgual()
    {
        // Sin telemetría de nadie no hay a quién reasignar, pero el rechazo se registró: la
        // API no puede responder que falló cuando el pedido efectivamente volvió a búsqueda.
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RepartidorId);

        var resultado = await CrearServicio().RechazarAsync(pedido.PedidoId, RepartidorId, "no puedo atenderlo");

        resultado.RechazoRegistrado.Should().BeTrue();
        resultado.Asignado.Should().BeFalse("no había otro domiciliario en el radio");

        (await _db.PedidosDisponibles.SingleAsync()).Estado.Should().Be(PedidoDisponible.EstadoBuscando);
    }

    [Fact]
    public async Task RechazarUnPedidoAjeno_NoRegistraNada()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoAsignado, RepartidorId);

        var resultado = await CrearServicio().RechazarAsync(pedido.PedidoId, OtroRepartidorId, "no es mío");

        resultado.RechazoRegistrado.Should().BeFalse();
        (await _db.PedidosDisponibles.SingleAsync()).Estado.Should().Be(PedidoDisponible.EstadoAsignado);
    }

    [Fact]
    public async Task CerrarEntrega_MarcaEntregadoPublicaElEventoYRegistraLaFecha()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RepartidorId);
        await _store.ActualizarAsync(RepartidorId, Lat, Lon, DateTime.UtcNow);

        var resultado = await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, TokenEntrega);

        resultado.Cerrado.Should().BeTrue();
        resultado.Estado.Should().Be(PedidoDisponible.EstadoEntregado);
        resultado.FechaEntrega.Should().NotBeNull();
        resultado.RepartidorId.Should().Be(RepartidorId);

        var guardado = await _db.PedidosDisponibles.SingleAsync();
        guardado.Estado.Should().Be(PedidoDisponible.EstadoEntregado);

        var asignacion = await _db.AsignacionesRepartidor.SingleAsync();
        asignacion.FechaEntrega.Should().NotBeNull();

        _publish.Verify(p => p.Publish(
            It.Is<PedidoEntregado>(e =>
                e.PedidoId == pedido.PedidoId &&
                e.RepartidorId == RepartidorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CerrarEntrega_LimpiaLaTelemetriaSoloDelDomiciliarioQueEntrego()
    {
        // FR-003.10. La clave GEO es compartida: hay que sacar el miembro, no la clave.
        await _store.ActualizarAsync(RepartidorId, Lat, Lon, DateTime.UtcNow);
        await _store.ActualizarAsync(OtroRepartidorId, Lat + 0.01, Lon, DateTime.UtcNow);
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RepartidorId);

        await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, TokenEntrega);

        (await _store.ObtenerAsync(RepartidorId)).Should().BeNull(
            "su última posición dejó de ser válida al cerrar la entrega");
        (await _store.ObtenerAsync(OtroRepartidorId)).Should().NotBeNull(
            "el otro domiciliario sigue en ruta y no tiene nada que ver con este cierre");
    }

    [Fact]
    public async Task CerrarEntrega_ConTokenAjeno_RechazaYNoMueveNada()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RepartidorId);
        await _store.ActualizarAsync(RepartidorId, Lat, Lon, DateTime.UtcNow);

        var resultado = await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, new string('0', 32));

        resultado.Cerrado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.TokenInvalido);

        (await _db.PedidosDisponibles.SingleAsync()).Estado.Should().Be(PedidoDisponible.EstadoEnRuta);
        (await _store.ObtenerAsync(RepartidorId)).Should().NotBeNull("no se cerró nada");
        _publish.Verify(p => p.Publish(It.IsAny<PedidoEntregado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CerrarEntrega_SinHaberSalidoDelLocal_SeRechaza()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoConfirmadoEnLocal, RepartidorId);

        var resultado = await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, TokenEntrega);

        resultado.Cerrado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.EstadoInvalido);
    }

    [Fact]
    public async Task CerrarEntrega_UnPedidoYaEntregado_SeRechazaSinTocarLaTelemetria()
    {
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoEntregado, RepartidorId);
        await _store.ActualizarAsync(RepartidorId, Lat, Lon, DateTime.UtcNow);

        var resultado = await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, TokenEntrega);

        resultado.Cerrado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.EstadoInvalido);
        _publish.Verify(p => p.Publish(It.IsAny<PedidoEntregado>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CerrarEntrega_UnPedidoQueNoExiste_DevuelveNoEncontrado()
    {
        var resultado = await CrearServicio().CerrarEntregaAsync(Guid.NewGuid(), TokenEntrega);

        resultado.Cerrado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.PedidoNoEncontrado);
    }

    [Fact]
    public async Task ElTokenDelLocal_NoCierraLaEntrega()
    {
        // Son de partes distintas: el del local lo escanea el domiciliario al recoger.
        var pedido = await CrearPedidoAsync(PedidoDisponible.EstadoEnRuta, RepartidorId);

        var resultado = await CrearServicio().CerrarEntregaAsync(pedido.PedidoId, TokenLocal);

        resultado.Cerrado.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoRechazo.TokenInvalido);
    }
}
