using Microsoft.Extensions.Options;

namespace Deuna.Delivery.Service.Services;

/// <summary>
/// Reintenta asignar los pedidos que quedaron en búsqueda (TASK-303, FR-003.8).
///
/// El flujo dice que el sistema busca "de forma activa": si al crear el pedido no había
/// nadie en el radio, el pedido no puede esperar indefinidamente a que alguien lo note.
/// Este servicio recorre los pedidos en búsqueda y vuelve a intentar el matching, así
/// un repartidor que aparece después (o que libera su entrega) recibe el pedido.
/// </summary>
public class AsignacionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AsignacionOptions _options;
    private readonly ILogger<AsignacionBackgroundService> _logger;

    public AsignacionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AsignacionOptions> options,
        ILogger<AsignacionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Un intervalo muy chico sería un bucle contra Redis; el mínimo lo protege.
        var intervalo = TimeSpan.FromSeconds(Math.Max(5, _options.IntervaloReintentoSegundos));

        _logger.LogInformation(
            "Reintento de asignación activo cada {Intervalo} s (radio {RadioKm} km)",
            intervalo.TotalSeconds, _options.RadioKm);

        using var temporizador = new PeriodicTimer(intervalo);

        while (await temporizador.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Un scope por vuelta: el DbContext y el servicio son scoped.
                using var scope = _scopeFactory.CreateScope();
                var asignacion = scope.ServiceProvider.GetRequiredService<IAsignacionService>();

                var asignados = await asignacion.ReintentarPendientesAsync(stoppingToken);
                if (asignados > 0)
                {
                    _logger.LogInformation("Reintento de asignación: {Asignados} pedido(s) asignado(s)", asignados);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo en una vuelta no debe tumbar el servicio: se registra y se sigue.
                _logger.LogError(ex, "Error en el reintento de asignación");
            }
        }
    }
}
