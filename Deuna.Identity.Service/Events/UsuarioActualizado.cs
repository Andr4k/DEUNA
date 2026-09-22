namespace Deuna.Identity.Service.Events;

public record UsuarioActualizado(
    Guid UsuarioId,
    string Rol,
    string Email,
    string? Telefono,
    bool Activo,
    DateTime OccurredAt
);