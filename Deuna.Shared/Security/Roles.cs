namespace Deuna.Shared.Security;

/// <summary>
/// Roles canónicos del sistema (spec FR-001.1: RESTAURANT, RIDER, ADMIN).
///
/// Los emite Identity en el claim `role` del JWT y los exigen las policies de
/// autorización de todos los servicios. Fuente única de verdad: un desajuste de
/// mayúsculas entre emisor y consumidor produce 403 en silencio.
/// </summary>
public static class Roles
{
    public const string Restaurant = "RESTAURANT";
    public const string Rider = "RIDER";
    public const string Admin = "ADMIN";
    public const string Customer = "CUSTOMER";
}
