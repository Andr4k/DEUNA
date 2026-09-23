using Deuna.Delivery.Service.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Deuna.Delivery.Service.Tests.Tracking;

[Collection(RedisCollection.Name)]
public class RedisTrackingStoreTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _fixture;
    private IConnectionMultiplexer _redis = null!;
    private RedisTrackingStore _store = null!;

    public RedisTrackingStoreTests(RedisContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _redis = _fixture.CrearConexion();
        await _redis.GetDatabase().KeyDeleteAsync(RedisTrackingStore.ClaveRepartidores);
        _store = new RedisTrackingStore(_redis, NullLogger<RedisTrackingStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task ActualizarAsync_GuardaLaPosicionDelRepartidor()
    {
        var riderId = Guid.NewGuid();

        await _store.ActualizarAsync(riderId, 4.6097, -74.0817, DateTime.UtcNow);

        var ubicacion = await _store.ObtenerAsync(riderId);
        ubicacion.Should().NotBeNull();
        ubicacion!.Latitud.Should().BeApproximately(4.6097, 0.0001);
        ubicacion.Longitud.Should().BeApproximately(-74.0817, 0.0001);
    }

    [Fact]
    public async Task ActualizarAsync_ExpiraLaClaveEnUnaHora()
    {
        await _store.ActualizarAsync(Guid.NewGuid(), 4.6097, -74.0817, DateTime.UtcNow);

        var ttl = await _redis.GetDatabase().KeyTimeToLiveAsync(RedisTrackingStore.ClaveRepartidores);

        ttl.Should().NotBeNull();
        ttl!.Value.TotalSeconds.Should().BeGreaterThan(3500).And.BeLessThanOrEqualTo(3600);
    }

    [Fact]
    public async Task ActualizarAsync_GuardaElTimestampDeLaTelemetria()
    {
        var riderId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 9, 23, 6, 20, 0, DateTimeKind.Utc);

        await _store.ActualizarAsync(riderId, 4.658, -74.056, timestamp);

        var ubicacion = await _store.ObtenerAsync(riderId);
        ubicacion!.ActualizadoEn.Should().BeCloseTo(timestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ActualizarAsync_LaUltimaPosicionReemplazaALaAnterior()
    {
        var riderId = Guid.NewGuid();

        await _store.ActualizarAsync(riderId, 4.6097, -74.0817, DateTime.UtcNow);
        await _store.ActualizarAsync(riderId, 4.7000, -74.0300, DateTime.UtcNow);

        var ubicacion = await _store.ObtenerAsync(riderId);
        ubicacion!.Latitud.Should().BeApproximately(4.7000, 0.0001);
        ubicacion.Longitud.Should().BeApproximately(-74.0300, 0.0001);
    }

    [Fact]
    public async Task ObtenerAsync_DevuelveNullSiNoHayTelemetria()
    {
        var ubicacion = await _store.ObtenerAsync(Guid.NewGuid());

        ubicacion.Should().BeNull();
    }

    [Fact]
    public async Task BuscarCercanoAsync_DevuelveElRepartidorMasProximoConSuDistancia()
    {
        var lejano = Guid.NewGuid();
        var cercano = Guid.NewGuid();

        // ~2.2 km al norte del punto de consulta
        await _store.ActualizarAsync(cercano, 4.6300, -74.0817, DateTime.UtcNow);
        // ~11 km al norte: fuera del radio de 5 km del plan técnico
        await _store.ActualizarAsync(lejano, 4.7100, -74.0817, DateTime.UtcNow);

        var resultado = await _store.BuscarCercanoAsync(4.6097, -74.0817, radioKm: 5);

        resultado.Should().NotBeNull();
        resultado!.RiderId.Should().Be(cercano);
        resultado.DistanciaMetros.Should().BeGreaterThan(2000).And.BeLessThan(2500);
    }

    [Fact]
    public async Task BuscarCercanoAsync_DevuelveNullSiNadieEstaEnElRadio()
    {
        await _store.ActualizarAsync(Guid.NewGuid(), 4.7100, -74.0817, DateTime.UtcNow);

        var resultado = await _store.BuscarCercanoAsync(4.6097, -74.0817, radioKm: 5);

        resultado.Should().BeNull();
    }
}
