using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Orders.Service.Models;
using Deuna.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

// Los tests comparten un único contenedor y una única base de datos, y cada test
// la limpia por completo antes de correr. Correrlos en paralelo haría que un test
// truncara las tablas mientras otro las está usando.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Deuna.Orders.Service.Tests;

/// <summary>
/// Contenedor PostGIS compartido por toda la suite.
/// Se usa PostGIS real (no InMemory) porque <c>GeoService</c> ejecuta SQL de PostGIS
/// (<c>geo.distance_meters</c>, <c>geo.point_in_polygon</c>) que el proveedor InMemory
/// no puede resolver.
/// </summary>
internal static class PostgisTestContainer
{
    private static readonly Lazy<PostgreSqlContainer> Instance = new(() =>
    {
        var container = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine")
            .WithDatabase("deuna_orders_test")
            .WithUsername("deuna_user")
            .WithPassword("deuna_test_2026")
            .Build();

        container.StartAsync().GetAwaiter().GetResult();
        return container;
    });

    public static string ConnectionString => Instance.Value.GetConnectionString();
}

/// <summary>
/// Datos y operaciones compartidas por los tests de Orders.
/// </summary>
internal static class TestData
{
    /// <summary>
    /// GUID del restaurante de prueba. El literal que usaban los tests originales
    /// ("a1b2c3d4-5e6f-7g8h-9i0j-k1l2m3n4o5p6") no es un GUID válido: contiene letras
    /// fuera del rango hexadecimal y <c>Guid.Parse</c> lanzaba FormatException.
    /// </summary>
    public static readonly Guid RestauranteId = Guid.Parse("a1b2c3d4-5e6f-4a7b-8c9d-e1f2a3b4c5d6");

    public const decimal RestauranteLatitud = 4.6097100m;
    public const decimal RestauranteLongitud = -74.0817500m;

    private static readonly string[] DiasSemana =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    /// <summary>
    /// Limpia todas las tablas conservando el esquema y las funciones de PostGIS.
    ///
    /// No se usa EnsureDeleted/EnsureCreated porque eso recrea la base desde el modelo
    /// y perdería las funciones <c>geo.*</c>, que viven en una migración SQL.
    ///
    /// Se excluyen las tablas que pertenecen a una extensión: <c>spatial_ref_sys</c> es
    /// de PostGIS y si se vacía, cualquier consulta geográfica falla con
    /// "Cannot find SRID (4326) in spatial_ref_sys".
    /// </summary>
    public static async Task ResetAsync(OrdersDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE r RECORD;
            BEGIN
                FOR r IN
                    SELECT c.relname
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relkind = 'r'
                      AND c.relname <> '__EFMigrationsHistory'
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_depend d
                          WHERE d.objid = c.oid
                            AND d.classid = 'pg_class'::regclass
                            AND d.deptype = 'e'
                      )
                LOOP
                    EXECUTE 'TRUNCATE TABLE public.' || quote_ident(r.relname) || ' CASCADE';
                END LOOP;
            END $$;
            """);
    }

    /// <summary>
    /// Siembra el restaurante replicado con horario abierto 24 h los 7 días.
    /// La validación de horario (<c>PedidoService.EstaEnHorarioAtencion</c>) lee
    /// <c>HorariosAtencion</c> del día actual: sin horario para hoy el pedido se
    /// rechaza con 400 "fuera de horario", y el resultado dependería de la hora
    /// a la que se corran los tests.
    /// </summary>
    public static async Task SeedRestauranteAsync(OrdersDbContext db)
    {
        var restaurante = new RestauranteReplicado
        {
            Id = RestauranteId,
            NombreComercial = "Demo Restaurant",
            RazonSocial = "Demo Restaurant S.A.S.",
            Nit = "900123456-7",
            DireccionSede = "Calle 45 #23-10",
            Ciudad = "Bogotá",
            Latitud = RestauranteLatitud,
            Longitud = RestauranteLongitud,
            Ubicacion = new NetTopologySuite.Geometries.Point(
                (double)RestauranteLongitud, (double)RestauranteLatitud) { SRID = 4326 },
            RadioCoberturaKm = 10.0m,
            AceptaPedidos = true,
            Activo = true,
            HoraApertura = new TimeSpan(8, 0, 0),
            HoraCierre = new TimeSpan(22, 0, 0),
            CreatedAt = DateTime.UtcNow,
            HorariosAtencion = DiasSemana.Select(dia => new HorarioAtencionReplicado
            {
                RestauranteReplicadoId = RestauranteId,
                DiaSemana = dia,
                HoraInicio = TimeSpan.Zero,
                HoraFin = new TimeSpan(23, 59, 59),
                Cerrado = false
            }).ToList()
        };

        db.RestaurantesReplicados.Add(restaurante);
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Emite JWT reales firmados con la misma clave que valida el servicio.
/// Los tests originales enviaban "test-restaurant-token" como Bearer, que el
/// pipeline de autenticación rechaza con 401: no era un token válido.
/// </summary>
internal static class TestJwt
{
    public const string SecretKey = "TestSecretKeyForTestingPurposesOnly123456789";
    public const string Issuer = "Deuna.Test";
    public const string Audience = "Deuna.Test";

    public static string CreateRestaurantToken(Guid restauranteId) =>
        CreateToken(restauranteId, Roles.Restaurant, "restaurant@test.com");

    /// <summary>
    /// Token con rol ADMIN. Las rutas del panel de administración exigen la política
    /// "admin" (Roles.Admin), que el token de restaurante no satisface: sin este emisor
    /// no habría forma de probar una ruta de administrador desde los tests.
    /// </summary>
    public static string CreateAdminToken(Guid adminId) =>
        CreateToken(adminId, Roles.Admin, "admin@test.com");

    private static string CreateToken(Guid subject, string role, string email)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject.ToString()),
                new Claim("role", role),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
