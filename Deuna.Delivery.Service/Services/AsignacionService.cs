using Deuna.Delivery.Service.Models;
using Deuna.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deuna.Delivery.Service.Services;

/// <summary>Configuración de la asignación automática (FR-003.7).</summary>
public class AsignacionOptions
{
    public const string Seccion = "Asignacion";

    /// <summary>
    /// Radio de búsqueda en km. Configurable a propósito: el flujo lo declara en 5 km
    /// pero admite cambios, así que no puede ser una constante de código.
    /// </summary>
    public double RadioKm { get; set; } = 5;

    /// <summary>Cada cuánto se reintenta asignar los pedidos que quedaron en búsqueda.</summary>
    public int IntervaloReintentoSegundos { get; set; } = 30;
}

public record ResultadoAsignacion(
    bool Asignado,
    string Mensaje,
    Guid? RepartidorId = null,
    double? DistanciaMetros = null,
    /// <summary>
    /// El rechazo quedó registrado, haya o no a quién reasignar. Sin esta marca, un rechazo
    /// correcto en el que no hay otro domiciliario disponible se confundía con un rechazo
    /// inválido.
    /// </summary>
    bool RechazoRegistrado = false);

/// <summary>Pedido asignado, tal como lo ve el repartidor en su app.</summary>
public record PedidoAsignadoDto(
    Guid PedidoId,
    string Codigo,
    string Estado,
    decimal Total,
    Guid RestauranteId,
    double? LatitudEntrega,
    double? LongitudEntrega,
    double? DistanciaMetros,
    DateTime? AsignadoAt,
    DateTime FechaCreacion,
    /// <summary>
    /// Token que el domiciliario muestra al cliente al entregar (TASK-305). Se le entrega
    /// porque tiene que mostrarlo; el token del local, en cambio, no viaja acá.
    /// </summary>
    string? TokenQrEntrega = null);

/// <summary>
/// Motivo del rechazo. Va tipado para que la capa HTTP elija el código de estado sin
/// adivinar por el texto del mensaje.
/// </summary>
public enum MotivoRechazo
{
    Ninguno,
    PedidoNoEncontrado,
    NoAsignado,
    EstadoInvalido,
    TokenInvalido
}

/// <summary>
/// Resultado de validar el QR del local. El mensaje explica el rechazo para que la app
/// pueda mostrarlo: "no es tu pedido" y "el token no sirve" son cosas distintas.
/// </summary>
public record ResultadoValidacionQr(
    bool Valido,
    string Mensaje,
    MotivoRechazo Motivo = MotivoRechazo.Ninguno,
    string? Estado = null,
    DateTime? FechaLlegadaLocal = null);

/// <summary>Resultado de iniciar la entrega (TASK-305).</summary>
public record ResultadoInicioEntrega(
    bool Iniciada,
    string Mensaje,
    MotivoRechazo Motivo = MotivoRechazo.Ninguno,
    string? Estado = null);

/// <summary>
/// Resultado del cierre por QR. Devuelve el repartidor porque quien cierra es el cliente,
/// que no tiene sesión: sin ese dato no se puede limpiar su telemetría.
/// </summary>
public record ResultadoCierreEntrega(
    bool Cerrado,
    string Mensaje,
    MotivoRechazo Motivo = MotivoRechazo.Ninguno,
    string? Estado = null,
    DateTime? FechaEntrega = null,
    Guid? RepartidorId = null);

/// <summary>
/// Asignación automática del pedido al domiciliario disponible más cercano (TASK-303).
///
/// El sistema busca y asigna; el domiciliario no toma pedidos de una lista (ADR-006).
/// Solo se consideran candidatos con telemetría vigente (viven en el GEO set de Redis,
/// que expira solo) y sin una entrega activa.
/// </summary>
public interface IAsignacionService
{
    /// <summary>Intenta asignar un pedido. Si no hay candidatos, lo deja en búsqueda.</summary>
    Task<ResultadoAsignacion> IntentarAsignarAsync(Guid pedidoId, Guid? excluirRepartidorId = null, CancellationToken cancellationToken = default);

    /// <summary>Reintenta asignar todos los pedidos que quedaron en búsqueda.</summary>
    Task<int> ReintentarPendientesAsync(CancellationToken cancellationToken = default);

    /// <summary>Registra el rechazo del repartidor asignado y dispara la reasignación.</summary>
    Task<ResultadoAsignacion> RechazarAsync(Guid pedidoId, Guid repartidorId, string? motivo, CancellationToken cancellationToken = default);

    /// <summary>Pedidos asignados a un repartidor, con la distancia a la que fue asignado.</summary>
    Task<IReadOnlyList<PedidoAsignadoDto>> ObtenerAsignadosAsync(Guid repartidorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida el QR que el restaurante muestra en el local (TASK-304). Confirma la presencia
    /// física del domiciliario asignado y habilita el inicio de la entrega.
    /// </summary>
    Task<ResultadoValidacionQr> ValidarQrLocalAsync(
        Guid pedidoId,
        Guid repartidorId,
        string tokenQrLocal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia la entrega: el pedido pasa a <c>EnRuta</c> y se publica el cambio de estado
    /// (TASK-305). Requiere haber confirmado antes la llegada al local.
    /// </summary>
    Task<ResultadoInicioEntrega> IniciarEntregaAsync(
        Guid pedidoId,
        Guid repartidorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cierra la entrega validando el QR que muestra el domiciliario (TASK-305).
    ///
    /// Es público a propósito: quien escanea es el cliente, que no tiene sesión. Registra la
    /// fecha de entrega, limpia la telemetría del domiciliario (FR-003.10) y publica
    /// <c>PedidoEntregado</c>.
    /// </summary>
    Task<ResultadoCierreEntrega> CerrarEntregaAsync(
        Guid pedidoId,
        string tokenQrEntrega,
        CancellationToken cancellationToken = default);
}

public class AsignacionService : IAsignacionService
{
    private readonly DeliveryDbContext _db;
    private readonly ITrackingStore _trackingStore;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly AsignacionOptions _options;
    private readonly ILogger<AsignacionService> _logger;

    public AsignacionService(
        DeliveryDbContext db,
        ITrackingStore trackingStore,
        IPublishEndpoint publishEndpoint,
        IOptions<AsignacionOptions> options,
        ILogger<AsignacionService> logger)
    {
        _db = db;
        _trackingStore = trackingStore;
        _publishEndpoint = publishEndpoint;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResultadoAsignacion> IntentarAsignarAsync(
        Guid pedidoId,
        Guid? excluirRepartidorId = null,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedidoId} no está proyectado en Delivery");
        }

        // Un pedido tiene un solo domiciliario activo: solo se asigna si sigue en búsqueda.
        if (pedido.Estado != PedidoDisponible.EstadoBuscando || pedido.RepartidorId is not null)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedido.Codigo} no está en búsqueda (estado {pedido.Estado})");
        }

        if (pedido.Latitud is null || pedido.Longitud is null)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedido.Codigo} no tiene punto de entrega: no se puede calcular cercanía");
        }

        var candidatos = await _trackingStore.BuscarCandidatosAsync(
            pedido.Latitud.Value, pedido.Longitud.Value, _options.RadioKm, cancellationToken);

        if (candidatos.Count == 0)
        {
            _logger.LogInformation(
                "Pedido {Codigo} sin repartidores con telemetría en {RadioKm} km: queda en búsqueda",
                pedido.Codigo, _options.RadioKm);

            return new ResultadoAsignacion(false, $"No hay repartidores con telemetría vigente en {_options.RadioKm} km");
        }

        var ocupados = await RepartidoresOcupadosAsync(cancellationToken);

        // Los candidatos vienen ordenados por distancia: el primero libre es el más cercano.
        var elegido = candidatos.FirstOrDefault(c =>
            c.RiderId != excluirRepartidorId && !ocupados.Contains(c.RiderId));

        if (elegido is null)
        {
            _logger.LogInformation(
                "Pedido {Codigo}: {Candidatos} candidatos en el radio, todos ocupados o excluidos",
                pedido.Codigo, candidatos.Count);

            return new ResultadoAsignacion(false, "No hay repartidores disponibles en el radio: todos tienen una entrega activa");
        }

        await AsignarAsync(pedido, elegido, cancellationToken);

        return new ResultadoAsignacion(
            true,
            $"Pedido {pedido.Codigo} asignado al repartidor más cercano",
            elegido.RiderId,
            elegido.DistanciaMetros);
    }

    public async Task<int> ReintentarPendientesAsync(CancellationToken cancellationToken = default)
    {
        var pendientes = await _db.PedidosDisponibles
            .Where(p => p.Estado == PedidoDisponible.EstadoBuscando
                        && p.RepartidorId == null
                        && p.Latitud != null
                        && p.Longitud != null)
            .Select(p => p.PedidoId)
            .ToListAsync(cancellationToken);

        var asignados = 0;
        foreach (var pedidoId in pendientes)
        {
            var resultado = await IntentarAsignarAsync(pedidoId, cancellationToken: cancellationToken);
            if (resultado.Asignado)
            {
                asignados++;
            }
        }

        return asignados;
    }

    public async Task<ResultadoAsignacion> RechazarAsync(
        Guid pedidoId,
        Guid repartidorId,
        string? motivo,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedidoId} no está proyectado en Delivery");
        }

        if (pedido.RepartidorId != repartidorId)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedido.Codigo} no está asignado a este repartidor");
        }

        // El rechazo solo vale antes de llegar al local: una vez confirmada la recogida,
        // el pedido ya está en manos del repartidor.
        if (pedido.Estado != PedidoDisponible.EstadoAsignado)
        {
            return new ResultadoAsignacion(false, $"El pedido {pedido.Codigo} ya no se puede rechazar (estado {pedido.Estado})");
        }

        var asignacion = await _db.AsignacionesRepartidor
            .Where(a => a.PedidoId == pedidoId
                        && a.RepartidorId == repartidorId
                        && a.Estado == AsignacionRepartidor.EstadoAsignado)
            .OrderByDescending(a => a.FechaAsignacion)
            .FirstOrDefaultAsync(cancellationToken);

        var ahora = DateTime.UtcNow;
        if (asignacion is not null)
        {
            asignacion.Estado = AsignacionRepartidor.EstadoRechazado;
            asignacion.FechaRechazo = ahora;
            asignacion.MotivoRechazo = motivo;
        }

        pedido.RepartidorId = null;
        pedido.Estado = PedidoDisponible.EstadoBuscando;
        pedido.AsignadoAt = null;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} rechazado por {RepartidorId}: vuelve a búsqueda",
            pedido.Codigo, repartidorId);

        // El rechazo es una transición de la máquina de estados (Asignado → Buscando), así que
        // se publica: sin esto, los demás servicios siguen mostrando el pedido como asignado.
        await _publishEndpoint.Publish(
            new PedidoActualizado(
                PedidoId: pedido.PedidoId,
                Codigo: pedido.Codigo,
                EstadoAnterior: PedidoDisponible.EstadoAsignado,
                EstadoNuevo: PedidoDisponible.EstadoBuscando,
                OccurredAt: ahora,
                Motivo: motivo),
            cancellationToken);

        // Reasignación inmediata, excluyendo a quien acaba de rechazar: volver a ofrecérselo
        // sería pedirle que rechace dos veces.
        var reasignacion = await IntentarAsignarAsync(pedidoId, excluirRepartidorId: repartidorId, cancellationToken);

        return reasignacion.Asignado
            ? reasignacion with
            {
                Mensaje = $"Rechazo registrado. {reasignacion.Mensaje}",
                RechazoRegistrado = true
            }
            : new ResultadoAsignacion(
                false,
                $"Rechazo registrado. El pedido vuelve a búsqueda: {reasignacion.Mensaje}",
                RechazoRegistrado: true);
    }

    public async Task<IReadOnlyList<PedidoAsignadoDto>> ObtenerAsignadosAsync(
        Guid repartidorId,
        CancellationToken cancellationToken = default)
    {
        var pedidos = await _db.PedidosDisponibles
            .Where(p => p.RepartidorId == repartidorId
                        && PedidoDisponible.EstadosQueOcupanAlRepartidor.Contains(p.Estado))
            .OrderBy(p => p.AsignadoAt)
            .ToListAsync(cancellationToken);

        if (pedidos.Count == 0)
        {
            return [];
        }

        var pedidoIds = pedidos.Select(p => p.PedidoId).ToList();
        var distancias = await _db.AsignacionesRepartidor
            .Where(a => pedidoIds.Contains(a.PedidoId)
                        && a.RepartidorId == repartidorId
                        && a.Estado == AsignacionRepartidor.EstadoAsignado)
            .ToDictionaryAsync(a => a.PedidoId, a => a.DistanciaMetros, cancellationToken);

        return pedidos
            .Select(p => new PedidoAsignadoDto(
                p.PedidoId,
                p.Codigo,
                p.Estado,
                p.Total,
                p.RestauranteId,
                p.Latitud,
                p.Longitud,
                distancias.TryGetValue(p.PedidoId, out var distancia) ? distancia : null,
                p.AsignadoAt,
                p.FechaCreacion,
                p.TokenQrEntrega))
            .ToList();
    }

    public async Task<ResultadoValidacionQr> ValidarQrLocalAsync(
        Guid pedidoId,
        Guid repartidorId,
        string tokenQrLocal,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return new ResultadoValidacionQr(
                false,
                $"El pedido {pedidoId} no está proyectado en Delivery",
                MotivoRechazo.PedidoNoEncontrado);
        }

        // Solo el domiciliario asignado: el QR prueba que llegó quien tiene el pedido.
        if (pedido.RepartidorId != repartidorId)
        {
            return new ResultadoValidacionQr(
                false,
                $"El pedido {pedido.Codigo} no está asignado a este domiciliario",
                MotivoRechazo.NoAsignado);
        }

        // La validación es de una sola vez: repetirla no puede volver a mover el estado ni
        // reescribir la hora de llegada.
        if (pedido.Estado != PedidoDisponible.EstadoAsignado)
        {
            return new ResultadoValidacionQr(
                false,
                pedido.Estado == PedidoDisponible.EstadoConfirmadoEnLocal
                    ? $"La llegada del pedido {pedido.Codigo} ya estaba confirmada"
                    : $"El pedido {pedido.Codigo} no está en estado de recogida (estado {pedido.Estado})",
                MotivoRechazo.EstadoInvalido);
        }

        // Comparación ordinal: el token es de un solo uso, está atado a un pedido concreto y
        // el endpoint exige el JWT del domiciliario asignado, así que un canal lateral de
        // tiempo no aporta nada. Se recorta porque un QR escaneado puede traer espacios.
        if (!string.Equals(pedido.TokenQrLocal, tokenQrLocal.Trim(), StringComparison.Ordinal))
        {
            return new ResultadoValidacionQr(
                false,
                $"El token del QR no corresponde al pedido {pedido.Codigo}",
                MotivoRechazo.TokenInvalido);
        }

        var ahora = DateTime.UtcNow;

        var asignacion = await _db.AsignacionesRepartidor
            .Where(a => a.PedidoId == pedidoId
                        && a.RepartidorId == repartidorId
                        && a.Estado == AsignacionRepartidor.EstadoAsignado)
            .OrderByDescending(a => a.FechaAsignacion)
            .FirstOrDefaultAsync(cancellationToken);

        if (asignacion is not null)
        {
            asignacion.Estado = AsignacionRepartidor.EstadoConfirmadoEnLocal;
            asignacion.FechaLlegadaLocal = ahora;
        }

        pedido.Estado = PedidoDisponible.EstadoConfirmadoEnLocal;

        await _db.SaveChangesAsync(cancellationToken);

        // La llegada al local es una transición de la máquina de estados
        // (Asignado → ConfirmadoEnLocal): sin publicarla, los demás servicios siguen viendo el
        // pedido como recién asignado y el hito no queda en su historial.
        await _publishEndpoint.Publish(
            new PedidoActualizado(
                PedidoId: pedido.PedidoId,
                Codigo: pedido.Codigo,
                EstadoAnterior: PedidoDisponible.EstadoAsignado,
                EstadoNuevo: PedidoDisponible.EstadoConfirmadoEnLocal,
                OccurredAt: ahora),
            cancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo}: llegada confirmada en el local por {RepartidorId}",
            pedido.Codigo, repartidorId);

        return new ResultadoValidacionQr(
            true,
            $"Llegada confirmada para el pedido {pedido.Codigo}. Ya puedes iniciar la entrega.",
            Estado: pedido.Estado,
            FechaLlegadaLocal: ahora);
    }

    public async Task<ResultadoInicioEntrega> IniciarEntregaAsync(
        Guid pedidoId,
        Guid repartidorId,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return new ResultadoInicioEntrega(
                false,
                $"El pedido {pedidoId} no está proyectado en Delivery",
                MotivoRechazo.PedidoNoEncontrado);
        }

        if (pedido.RepartidorId != repartidorId)
        {
            return new ResultadoInicioEntrega(
                false,
                $"El pedido {pedido.Codigo} no está asignado a este domiciliario",
                MotivoRechazo.NoAsignado);
        }

        // La entrega arranca después de confirmar la llegada al local: si no, el domiciliario
        // podría ponerse en ruta sin haber recogido el pedido.
        if (pedido.Estado != PedidoDisponible.EstadoConfirmadoEnLocal)
        {
            return new ResultadoInicioEntrega(
                false,
                pedido.Estado == PedidoDisponible.EstadoEnRuta
                    ? $"El pedido {pedido.Codigo} ya está en ruta"
                    : $"El pedido {pedido.Codigo} todavía no está confirmado en el local (estado {pedido.Estado})",
                MotivoRechazo.EstadoInvalido);
        }

        var ahora = DateTime.UtcNow;

        var asignacion = await AsignacionActivaAsync(pedidoId, repartidorId, cancellationToken);
        if (asignacion is not null)
        {
            asignacion.FechaInicioEntrega = ahora;
        }

        pedido.Estado = PedidoDisponible.EstadoEnRuta;

        await _db.SaveChangesAsync(cancellationToken);

        await _publishEndpoint.Publish(
            new PedidoActualizado(
                PedidoId: pedido.PedidoId,
                Codigo: pedido.Codigo,
                EstadoAnterior: PedidoDisponible.EstadoConfirmadoEnLocal,
                EstadoNuevo: PedidoDisponible.EstadoEnRuta,
                OccurredAt: ahora),
            cancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo}: entrega iniciada por {RepartidorId}", pedido.Codigo, repartidorId);

        return new ResultadoInicioEntrega(
            true,
            $"Entrega del pedido {pedido.Codigo} iniciada.",
            Estado: pedido.Estado);
    }

    public async Task<ResultadoCierreEntrega> CerrarEntregaAsync(
        Guid pedidoId,
        string tokenQrEntrega,
        CancellationToken cancellationToken = default)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return new ResultadoCierreEntrega(
                false,
                $"El pedido {pedidoId} no está proyectado en Delivery",
                MotivoRechazo.PedidoNoEncontrado);
        }

        // Cierra una entrega en curso: el pedido tiene que haber salido del local.
        if (pedido.Estado != PedidoDisponible.EstadoEnRuta)
        {
            return new ResultadoCierreEntrega(
                false,
                pedido.Estado == PedidoDisponible.EstadoEntregado
                    ? $"El pedido {pedido.Codigo} ya estaba entregado"
                    : $"El pedido {pedido.Codigo} no está en ruta (estado {pedido.Estado})",
                MotivoRechazo.EstadoInvalido);
        }

        if (!string.Equals(pedido.TokenQrEntrega, tokenQrEntrega.Trim(), StringComparison.Ordinal))
        {
            return new ResultadoCierreEntrega(
                false,
                $"El token del QR no corresponde al pedido {pedido.Codigo}",
                MotivoRechazo.TokenInvalido);
        }

        var ahora = DateTime.UtcNow;
        var repartidorId = pedido.RepartidorId;

        if (repartidorId is not null)
        {
            var asignacion = await AsignacionActivaAsync(pedidoId, repartidorId.Value, cancellationToken);
            if (asignacion is not null)
            {
                asignacion.FechaEntrega = ahora;
            }
        }

        pedido.Estado = PedidoDisponible.EstadoEntregado;

        await _db.SaveChangesAsync(cancellationToken);

        // FR-003.10: la última posición del domiciliario deja de ser válida cuando ya no está
        // en ruta. Un fallo al limpiar no puede impedir cerrar la entrega —el pedido ya está
        // entregado y el TTL de Redis termina borrando la telemetría igual—, así que se
        // registra y se sigue.
        if (repartidorId is not null)
        {
            try
            {
                await _trackingStore.EliminarAsync(repartidorId.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo limpiar la telemetría del repartidor {RepartidorId} tras cerrar la entrega {Codigo}",
                    repartidorId, pedido.Codigo);
            }
        }

        // Quien cierra es el cliente, sin sesión: el repartidor sale del pedido, no del token.
        if (repartidorId is not null)
        {
            await _publishEndpoint.Publish(
                new PedidoEntregado(
                    PedidoId: pedido.PedidoId,
                    Codigo: pedido.Codigo,
                    RepartidorId: repartidorId.Value,
                    FechaEntrega: ahora,
                    OccurredAt: ahora),
                cancellationToken);
        }

        _logger.LogInformation(
            "Pedido {Codigo}: entrega cerrada (repartidor {RepartidorId})", pedido.Codigo, repartidorId);

        return new ResultadoCierreEntrega(
            true,
            $"Entrega del pedido {pedido.Codigo} cerrada. ¡Gracias!",
            Estado: pedido.Estado,
            FechaEntrega: ahora,
            RepartidorId: repartidorId);
    }

    /// <summary>
    /// Asignación activa del pedido para ese repartidor: la última que no fue rechazada.
    /// El estado del pedido vive en <see cref="PedidoDisponible"/>; esta fila es su historial,
    /// así que no se le duplica la máquina de estados.
    /// </summary>
    private Task<AsignacionRepartidor?> AsignacionActivaAsync(
        Guid pedidoId,
        Guid repartidorId,
        CancellationToken cancellationToken) =>
        _db.AsignacionesRepartidor
            .Where(a => a.PedidoId == pedidoId
                        && a.RepartidorId == repartidorId
                        && a.Estado != AsignacionRepartidor.EstadoRechazado)
            .OrderByDescending(a => a.FechaAsignacion)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Repartidores con una entrega activa: no pueden recibir otra (FR-003.1).</summary>
    private async Task<HashSet<Guid>> RepartidoresOcupadosAsync(CancellationToken cancellationToken)
    {
        var ocupados = await _db.PedidosDisponibles
            .Where(p => p.RepartidorId != null && PedidoDisponible.EstadosQueOcupanAlRepartidor.Contains(p.Estado))
            .Select(p => p.RepartidorId!.Value)
            .ToListAsync(cancellationToken);

        return [.. ocupados];
    }

    private async Task AsignarAsync(PedidoDisponible pedido, UbicacionGps elegido, CancellationToken cancellationToken)
    {
        var ahora = DateTime.UtcNow;

        // Historial: deja la trazabilidad de la asignación y de sus rechazos previos.
        _db.AsignacionesRepartidor.Add(new AsignacionRepartidor
        {
            PedidoId = pedido.PedidoId,
            RepartidorId = elegido.RiderId,
            Estado = AsignacionRepartidor.EstadoAsignado,
            DistanciaMetros = elegido.DistanciaMetros,
            FechaAsignacion = ahora
        });

        pedido.RepartidorId = elegido.RiderId;
        pedido.Estado = PedidoDisponible.EstadoAsignado;
        pedido.AsignadoAt = ahora;

        await _db.SaveChangesAsync(cancellationToken);

        // El domiciliario se entera por su app (GET /my-orders) y el evento lo consumen
        // los demás servicios para mantener sus proyecciones al día.
        await _publishEndpoint.Publish(
            new PedidoAsignado(
                PedidoId: pedido.PedidoId,
                Codigo: pedido.Codigo,
                RepartidorId: elegido.RiderId,
                OccurredAt: ahora),
            cancellationToken);

        _logger.LogInformation(
            "Pedido {Codigo} asignado automáticamente al repartidor {RepartidorId} a {Distancia:F0} m",
            pedido.Codigo, elegido.RiderId, elegido.DistanciaMetros ?? 0);
    }
}
