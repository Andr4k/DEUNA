using Deuna.Notification.Service.Models;
using Deuna.Notification.Service.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deuna.Notification.Service.Tests;

/// <summary>
/// El servicio resuelve a quién notificar y registra el resultado; el envío real lo hace
/// un proveedor detrás de <see cref="IPushSender"/>. Estas pruebas usan un proveedor falso
/// para verificar la lógica sin depender de FCM.
/// </summary>
public class NotificacionServiceTests : IDisposable
{
    private static readonly Guid RiderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly NotificationDbContext _db;
    private readonly ProveedorFalso _proveedor = new();

    public NotificacionServiceTests()
    {
        _db = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"notificaciones-{Guid.NewGuid()}")
            .Options);
    }

    public void Dispose() => _db.Dispose();

    private INotificacionService CrearServicio() =>
        new NotificacionService(_db, _proveedor, NullLogger<NotificacionService>.Instance);

    private static MensajePush Mensaje() => new(
        Titulo: "Pedido asignado",
        Cuerpo: "PED-00042 · Restaurante Demo · a 999 m",
        Datos: new Dictionary<string, string> { ["pedidoId"] = Guid.NewGuid().ToString(), ["tipo"] = "PedidoAsignado" });

    // ------------------------------------------------------- registro de dispositivos

    [Fact]
    public async Task RegistraElDispositivoDelUsuario()
    {
        var servicio = CrearServicio();

        var dispositivo = await servicio.RegistrarDispositivoAsync(RiderId, "token-fcm-abc", "android");

        dispositivo.UsuarioId.Should().Be(RiderId);
        dispositivo.Token.Should().Be("token-fcm-abc");
        dispositivo.Plataforma.Should().Be("android");
        dispositivo.Activo.Should().BeTrue();

        (await _db.Dispositivos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegistrarElMismoTokenDosVeces_NoDuplica()
    {
        var servicio = CrearServicio();

        await servicio.RegistrarDispositivoAsync(RiderId, "token-fcm-abc", "android");
        await servicio.RegistrarDispositivoAsync(RiderId, "token-fcm-abc", "android");

        (await _db.Dispositivos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ElMismoTokenEnOtroUsuario_SeReasigna()
    {
        var otroUsuario = Guid.NewGuid();
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-compartido", "android");

        // El mismo teléfono: el domiciliario cierra sesión y entra otra persona.
        await servicio.RegistrarDispositivoAsync(otroUsuario, "token-compartido", "android");

        var dispositivos = await _db.Dispositivos.ToListAsync();
        dispositivos.Should().HaveCount(1, "un token pertenece a un solo dispositivo");
        dispositivos[0].UsuarioId.Should().Be(otroUsuario);
    }

    [Fact]
    public async Task UnDispositivoDesregistrado_NoRecibeNotificaciones()
    {
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-fcm-abc", "android");
        (await servicio.DesregistrarDispositivoAsync(RiderId, "token-fcm-abc")).Should().BeTrue();

        var resultado = await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje());

        resultado.Enviada.Should().BeFalse();
        resultado.Estado.Should().Be(NotificacionEnviada.EstadoSinDispositivo);
        _proveedor.Envios.Should().BeEmpty();
    }

    // -------------------------------------------------------------------- envío

    [Fact]
    public async Task NotificaTodosLosDispositivosActivosDelUsuario()
    {
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-1", "android");
        await servicio.RegistrarDispositivoAsync(RiderId, "token-2", "ios");

        var resultado = await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje());

        resultado.Enviada.Should().BeTrue();
        resultado.Dispositivos.Should().Be(2);
        _proveedor.Envios.Should().HaveCount(2);
        _proveedor.Envios.Select(e => e.Token).Should().BeEquivalentTo(["token-1", "token-2"]);
    }

    [Fact]
    public async Task SinDispositivos_QuedaRegistradoSinError()
    {
        var servicio = CrearServicio();

        var resultado = await servicio.NotificarAsync(Guid.NewGuid(), "PedidoAsignado", Mensaje());

        resultado.Enviada.Should().BeFalse();
        resultado.Estado.Should().Be(NotificacionEnviada.EstadoSinDispositivo);

        // Queda el rastro: si un domiciliario no recibe el pedido, hay que poder ver por qué.
        var registro = await _db.NotificacionesEnviadas.SingleAsync();
        registro.Estado.Should().Be(NotificacionEnviada.EstadoSinDispositivo);
    }

    [Fact]
    public async Task ElFalloDelProveedor_NoPropagaExcepcionYQuedaRegistrado()
    {
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-1", "android");
        _proveedor.Fallo = "FCM no responde";

        // No debe lanzar: un proveedor caído no puede tumbar el consumo del evento.
        var resultado = await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje());

        resultado.Enviada.Should().BeFalse();
        resultado.Estado.Should().Be(NotificacionEnviada.EstadoFallida);
        resultado.Error.Should().Contain("FCM no responde");

        var registro = await _db.NotificacionesEnviadas.SingleAsync();
        registro.Estado.Should().Be(NotificacionEnviada.EstadoFallida);
        registro.Error.Should().Contain("FCM no responde");
    }

    [Fact]
    public async Task UnTokenQueElProveedorRechaza_DesactivaElDispositivo()
    {
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-viejo", "android");
        _proveedor.TokenInvalido = true;

        await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje());

        var dispositivo = await _db.Dispositivos.SingleAsync();
        dispositivo.Activo.Should().BeFalse("el proveedor dijo que el token ya no existe: seguir usándolo es ruido");

        // El próximo envío ya no lo intenta.
        _proveedor.Envios.Clear();
        await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje());
        _proveedor.Envios.Should().BeEmpty();
    }

    [Fact]
    public async Task ElMismoMensajeId_NoSeReenvia()
    {
        var servicio = CrearServicio();
        await servicio.RegistrarDispositivoAsync(RiderId, "token-1", "android");
        var mensajeId = Guid.NewGuid();

        var primero = await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje(), mensajeId);
        var segundo = await servicio.NotificarAsync(RiderId, "PedidoAsignado", Mensaje(), mensajeId);

        primero.Enviada.Should().BeTrue();
        segundo.Enviada.Should().BeFalse("el mensaje ya se procesó");
        _proveedor.Envios.Should().HaveCount(1, "un reenvío de MassTransit no puede notificar dos veces");
    }

    /// <summary>Proveedor de mentira: registra lo que se le pidió enviar.</summary>
    private sealed class ProveedorFalso : IPushSender
    {
        public List<(string Token, MensajePush Mensaje)> Envios { get; } = [];
        public string? Fallo { get; set; }
        public bool TokenInvalido { get; set; }

        public Task<ResultadoPush> EnviarAsync(string token, MensajePush mensaje, CancellationToken cancellationToken = default)
        {
            Envios.Add((token, mensaje));

            if (TokenInvalido)
            {
                return Task.FromResult(new ResultadoPush(false, "falso", "token no registrado", TokenInvalido: true));
            }

            return Fallo is not null
                ? Task.FromResult(new ResultadoPush(false, "falso", Fallo))
                : Task.FromResult(new ResultadoPush(true, "falso"));
        }
    }
}
