using Deuna.Identity.Service.Events;

namespace Deuna.Identity.Service.Services;

public interface IEventPublisher
{
    Task PublishUsuarioRegistradoAsync(UsuarioRegistrado evento, CancellationToken cancellationToken = default);
    Task PublishUsuarioActualizadoAsync(UsuarioActualizado evento, CancellationToken cancellationToken = default);
}