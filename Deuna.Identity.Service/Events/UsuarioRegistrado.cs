namespace Deuna.Identity.Service.Events;

public record UsuarioRegistrado(
    Guid UsuarioId,
    string Rol,
    string Email,
    string? Telefono,
    bool Activo,
    DateTime OccurredAt
);