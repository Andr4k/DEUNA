using StackExchange.Redis;
using Testcontainers.Redis;

namespace Deuna.Delivery.Service.Tests.Tracking;

/// <summary>
/// Redis real en Docker (TestContainers) para los tests de integración de tracking,
/// tal como exige el criterio de aceptación de TASK-302.
/// </summary>
public class RedisContainerFixture : IAsyncLifetime
{
    public RedisContainer Container { get; private set; } = null!;

    /// <summary>Cadena de conexión del contenedor (host:puerto asignado por Docker).</summary>
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }

    public IConnectionMultiplexer CrearConexion() => ConnectionMultiplexer.Connect(ConnectionString);
}

[CollectionDefinition(Name)]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "redis-container";
}
