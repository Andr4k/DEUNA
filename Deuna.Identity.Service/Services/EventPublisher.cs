using Deuna.Identity.Service.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Deuna.Identity.Service.Services;

public class EventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(IPublishEndpoint publishEndpoint, ILogger<EventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishUsuarioRegistradoAsync(UsuarioRegistrado evento, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publicando evento UsuarioRegistrado para usuario {UsuarioId}", evento.UsuarioId);
        await _publishEndpoint.Publish(evento, cancellationToken);
    }

    public async Task PublishUsuarioActualizadoAsync(UsuarioActualizado evento, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publicando evento UsuarioActualizado para usuario {UsuarioId}", evento.UsuarioId);
        await _publishEndpoint.Publish(evento, cancellationToken);
    }
}