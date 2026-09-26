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

/// <summary>
/// Por qué una asignación no se pudo hacer.
///
/// Existe porque el panel de administración tiene que responder distinto según el caso:
/// un pedido que ya no está en búsqueda es un 400, y un repartidor que no es candidato es
/// un 409. Deducirlo del texto del mensaje sería frágil: el día que alguien corrija una
/// coma, el código HTTP cambia sin que nadie lo note. Sigue la misma idea que
/// <see cref="MotivoRechazo"/> en las validaciones de QR.
/// </summary>
public enum MotivoAsignacion
{
    /// <summary>Se asignó, o el motivo no aplica.</summary>
    Ninguno,

    /// <summary>El pedido no está proyectado en Delivery.</summary>
    PedidoNoEncontrado,

    /// <summary>El pedido ya no está en búsqueda: tiene repartidor o cambió de estado.</summary>
    EstadoInvalido,

    /// <summary>El pedido no tiene punto de entrega, así que no hay cercanía que calcular.</summary>
    SinPuntoEntrega,

    /// <summary>No hay repartidores con telemetría vigente dentro del radio.</summary>
    SinCandidatos,

    /// <summary>El repartidor elegido está fuera del radio o no tiene telemetría.</summary>
    RepartidorNoEsCandidato,

    /// <summary>El repartidor elegido está en el radio pero ya tiene una entrega activa.</summary>
    RepartidorOcupado
}

/// <summary>
/// Un repartidor que podría recibir el pedido, con lo que el operador necesita para elegir.
///
/// No trae "libera en": eso sería una estimación sin dato detrás. Trae <see cref="OcupadoDesde"/>,
/// que es un hecho, y el operador decide con eso. Tampoco trae una calificación del
/// repartidor: existe, pero vive en Feedback, no en Delivery.
/// </summary>
public record CandidatoAsignable(
    Guid RepartidorId,
    string? NombreCompleto,
    string? CiudadOperacion,
    double? DistanciaMetros,
    bool Disponible,
    string? ServicioActualCodigo,
    string? ServicioActualEstado,
    DateTime? OcupadoDesde);

/// <summary>
/// El resultado de pedirle a Delivery los candidatos de un pedido.
///
/// Reusa <see cref="MotivoAsignacion"/>: si el pedido no se puede asignar, esta lista no
/// tiene sentido, y la pantalla tiene que poder decir por qué en vez de mostrar una tabla
/// vacía que parece un problema de red.
/// </summary>
public record ResultadoCandidatos(
    bool Exito,
    string Mensaje,
    MotivoAsignacion Motivo = MotivoAsignacion.Ninguno,
    Guid? PedidoId = null,
    string? Codigo = null,
    double? RadioKm = null,
    IReadOnlyList<CandidatoAsignable>? Candidatos = null);

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
    bool RechazoRegistrado = false,
    /// <summary>
    /// Por qué no se asignó. Tipado para que el panel decida el código HTTP sin leer el
    /// texto del mensaje.
    /// </summary>
    MotivoAsignacion Motivo = MotivoAsignacion.Ninguno);

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

    /// <summary>
    /// Asigna un pedido al repartidor que elige un administrador desde el panel.
    ///
    /// No salta ninguna regla: el elegido tiene que estar dentro del radio del pedido y
    /// libre, igual que en la asignación automática. La diferencia es que acá el sistema no
    /// elige — el operador puede saber algo que el algoritmo no ve —, así que la elección
    /// se respeta en tanto y en cuanto sea un candidato válido.
    /// </summary>
    Task<ResultadoAsignacion> AsignarManualmenteAsync(Guid pedidoId, Guid repartidorId, CancellationToken cancellationToken = default);

    /// <summary>Lista los repartidores dentro del radio del pedido, con su disponibilidad.</summary>
    Task<ResultadoCandidatos> ObtenerCandidatosAsync(Guid pedidoId, CancellationToken cancellationToken = default);

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
        var (pedido, error) = await PedidoAsignableAsync(pedidoId, cancellationToken);
        if (pedido is null)
        {
            return error!;
        }

        var candidatos = await _trackingStore.BuscarCandidatosAsync(
            pedido.Latitud!.Value, pedido.Longitud!.Value, _options.RadioKm, cancellationToken);

        if (candidatos.Count == 0)
        {
            _logger.LogInformation(
                "Pedido {Codigo} sin repartidores con telemetría en {RadioKm} km: queda en búsqueda",
                pedido.Codigo, _options.RadioKm);

            return new ResultadoAsignacion(
                false,
                $"No hay repartidores con telemetría vigente en {_options.RadioKm} km",
                Motivo: MotivoAsignacion.SinCandidatos);
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

        await AsignarAsync(pedido, elegido, "automáticamente", cancellationToken);

        return new ResultadoAsignacion(
            true,
            $"Pedido {pedido.Codigo} asignado al repartidor más cercano",
            elegido.RiderId,
            elegido.DistanciaMetros);
    }

    public async Task<ResultadoAsignacion> AsignarManualmenteAsync(
        Guid pedidoId,
        Guid repartidorId,
        CancellationToken cancellationToken = default)
    {
        var (pedido, error) = await PedidoAsignableAsync(pedidoId, cancellationToken);
        if (pedido is null)
        {
            return error!;
        }

        // La lista sale del mismo lugar que en la asignación automática: el radio se valida
        // en Redis, así que un repartidor fuera del radio o sin telemetría no aparece acá.
        var candidatos = await _trackingStore.BuscarCandidatosAsync(
            pedido.Latitud!.Value, pedido.Longitud!.Value, _options.RadioKm, cancellationToken);

        // Estar en el radio no alcanza: el elegido también tiene que estar libre. Un
        // repartidor con una entrega activa está en el radio igual, así que la lista de
        // candidatos por sí sola lo incluiría. Es la misma condición que usa la asignación
        // automática para descartar a los ocupados.
        var ocupados = await RepartidoresOcupadosAsync(cancellationToken);
        var elegido = candidatos.FirstOrDefault(c =>
            c.RiderId == repartidorId && !ocupados.Contains(c.RiderId));

        if (elegido is null)
        {
            // Estar fuera del radio y estar ocupado son dos cosas distintas, y el operador
            // decide distinto en cada caso. Por eso el motivo se distingue, en lugar de
            // devolver un "no se pudo" que no le dice qué hacer.
            if (ocupados.Contains(repartidorId))
            {
                _logger.LogInformation(
                    "Pedido {Codigo}: el repartidor {RepartidorId} está dentro del radio pero ocupado",
                    pedido.Codigo, repartidorId);

                return new ResultadoAsignacion(
                    false,
                    $"El repartidor {repartidorId} tiene una entrega activa y no puede recibir otra",
                    Motivo: MotivoAsignacion.RepartidorOcupado);
            }

            _logger.LogInformation(
                "Pedido {Codigo}: el repartidor {RepartidorId} no está entre los {Candidatos} candidatos del radio de {RadioKm} km",
                pedido.Codigo, repartidorId, candidatos.Count, _options.RadioKm);

            return new ResultadoAsignacion(
                false,
                $"El repartidor {repartidorId} no está dentro del radio del pedido ni tiene telemetría vigente",
                Motivo: MotivoAsignacion.RepartidorNoEsCandidato);
        }

        await AsignarAsync(pedido, elegido, "a mano", cancellationToken);

        return new ResultadoAsignacion(
            true,
            $"Pedido {pedido.Codigo} asignado a mano al repartidor elegido",
            elegido.RiderId,
            elegido.DistanciaMetros);
    }

    /// <summary>
    /// El pedido, si está en condiciones de recibir una asignación.
    ///
    /// Las dos asignaciones —la automática y la manual— tienen que aceptar y rechazar
    /// exactamente lo mismo. Si estas validaciones vivieran en cada una, con el tiempo
    /// aceptarían cosas distintas y solo se notaría en producción.
    /// </summary>
    private async Task<(PedidoDisponible? Pedido, ResultadoAsignacion? Error)> PedidoAsignableAsync(
        Guid pedidoId,
        CancellationToken cancellationToken)
    {
        var pedido = await _db.PedidosDisponibles
            .FirstOrDefaultAsync(p => p.PedidoId == pedidoId, cancellationToken);

        if (pedido is null)
        {
            return (null, new ResultadoAsignacion(
                false,
                $"El pedido {pedidoId} no está proyectado en Delivery",
                Motivo: MotivoAsignacion.PedidoNoEncontrado));
        }

        // Un pedido tiene un solo domiciliario activo: solo se asigna si sigue en búsqueda.
        if (pedido.Estado != PedidoDisponible.EstadoBuscando || pedido.RepartidorId is not null)
        {
            return (null, new ResultadoAsignacion(
                false,
                $"El pedido {pedido.Codigo} no está en búsqueda (estado {pedido.Estado})",
                Motivo: MotivoAsignacion.EstadoInvalido));
        }

        if (pedido.Latitud is null || pedido.Longitud is null)
        {
            return (null, new ResultadoAsignacion(
                false,
                $"El pedido {pedido.Codigo} no tiene punto de entrega: no se puede calcular cercanía",
                Motivo: MotivoAsignacion.SinPuntoEntrega));
        }

        return (pedido, null);
    }

    public async Task<ResultadoCandidatos> ObtenerCandidatosAsync(
        Guid pedidoId,
        CancellationToken cancellationToken = default)
    {
        var (pedido, error) = await PedidoAsignableAsync(pedidoId, cancellationToken);
        if (pedido is null)
        {
            return new ResultadoCandidatos(false, error!.Mensaje, error.Motivo);
        }

        // La misma lista que usa la asignación: el radio se valida en Redis, así que lo que
        // el operador ve es exactamente el conjunto entre el que el sistema va a aceptar.
        var candidatos = await _trackingStore.BuscarCandidatosAsync(
            pedido.Latitud!.Value, pedido.Longitud!.Value, _options.RadioKm, cancellationToken);

        var ocupados = await RepartidoresOcupadosAsync(cancellationToken);

        var ids = candidatos.Select(c => c.RiderId).ToList();

        // Dos consultas para toda la lista, no una por candidato: con un radio grande, una
        // consulta por fila convierte una pantalla en decenas de viajes a la base.
        var perfiles = await _db.RepartidoresReplicados
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var serviciosActivos = await _db.PedidosDisponibles
            .Where(p => p.RepartidorId != null
                && ids.Contains(p.RepartidorId.Value)
                && PedidoDisponible.EstadosQueOcupanAlRepartidor.Contains(p.Estado))
            .ToListAsync(cancellationToken);

        var lista = candidatos
            .Select(c =>
            {
                var perfil = perfiles.GetValueOrDefault(c.RiderId);
                var servicio = serviciosActivos.FirstOrDefault(p => p.RepartidorId == c.RiderId);

                return new CandidatoAsignable(
                    RepartidorId: c.RiderId,
                    NombreCompleto: perfil?.NombreCompleto,
                    CiudadOperacion: perfil?.CiudadOperacion,
                    DistanciaMetros: c.DistanciaMetros,
                    Disponible: !ocupados.Contains(c.RiderId),
                    ServicioActualCodigo: servicio?.Codigo,
                    ServicioActualEstado: servicio?.Estado,
                    OcupadoDesde: servicio?.AsignadoAt);
            })
            .ToList();

        _logger.LogInformation(
            "Pedido {Codigo}: {Candidatos} candidatos en {RadioKm} km, {Libres} libres",
            pedido.Codigo, lista.Count, _options.RadioKm, lista.Count(c => c.Disponible));

        return new ResultadoCandidatos(
            true,
            $"{lista.Count} repartidor(es) con telemetría en {_options.RadioKm} km",
            PedidoId: pedido.PedidoId,
            Codigo: pedido.Codigo,
            RadioKm: _options.RadioKm,
            Candidatos: lista);
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

    private async Task AsignarAsync(PedidoDisponible pedido, UbicacionGps elegido, string origen, CancellationToken cancellationToken)
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
            "Pedido {Codigo} asignado {Origen} al repartidor {RepartidorId} a {Distancia:F0} m",
            pedido.Codigo, origen, elegido.RiderId, elegido.DistanciaMetros ?? 0);
    }
}
