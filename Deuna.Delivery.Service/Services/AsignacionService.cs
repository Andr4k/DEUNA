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
    double? DistanciaMetros = null);

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
    DateTime FechaCreacion);

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

        // Reasignación inmediata, excluyendo a quien acaba de rechazar: volver a ofrecérselo
        // sería pedirle que rechace dos veces.
        var reasignacion = await IntentarAsignarAsync(pedidoId, excluirRepartidorId: repartidorId, cancellationToken);

        return reasignacion.Asignado
            ? reasignacion with { Mensaje = $"Rechazo registrado. {reasignacion.Mensaje}" }
            : new ResultadoAsignacion(false, $"Rechazo registrado. El pedido vuelve a búsqueda: {reasignacion.Mensaje}");
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
                p.FechaCreacion))
            .ToList();
    }

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
