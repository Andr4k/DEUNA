using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Deuna.Identity.Service.Events;
using Deuna.Identity.Service.Models;
using Deuna.Identity.Service.Services;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Deuna.Identity.Service.Tests.Events;

public class IdentityEventsPublisherTests : IAsyncLifetime
{
    private readonly InMemoryTestHarness _harness;
    private IServiceProvider? _provider;

    public IdentityEventsPublisherTests()
    {
        _harness = new InMemoryTestHarness();
    }

    public async Task InitializeAsync()
    {
        await _harness.Start();
        
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddConsole());
        
        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseInMemoryDatabase("TestIdentityDb");
        });

        // Use the harness's bus for publishing
        services.AddSingleton<IBus>(_harness.Bus);
        services.AddSingleton<IPublishEndpoint>(_harness.Bus);
        
        services.AddSingleton(_harness);
        services.AddScoped<IEventPublisher, EventPublisher>();
        
        _provider = services.BuildServiceProvider();
        
        // Ensure database is created
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }

    [Fact]
    public async Task PublishUsuarioRegistradoAsync_PublishesEvent()
    {
        // Arrange
        var publisher = _provider.GetRequiredService<IEventPublisher>();
        var evento = new UsuarioRegistrado(
            UsuarioId: Guid.NewGuid(),
            Rol: "Restaurant",
            Email: "test@restaurant.com",
            Telefono: "+57300123456",
            Activo: true,
            OccurredAt: DateTime.UtcNow
        );

        // Act
        await publisher.PublishUsuarioRegistradoAsync(evento);

        // Assert - Event published exactly once
        var published = _harness.Published.Select<UsuarioRegistrado>().ToList();
        Assert.Single(published);

        var publishedEvent = published[0].Context.Message;
        Assert.Equal("Restaurant", publishedEvent.Rol);
        Assert.Equal("test@restaurant.com", publishedEvent.Email);
        Assert.Equal("+57300123456", publishedEvent.Telefono);
        Assert.True(publishedEvent.Activo);
        Assert.NotEqual(default, publishedEvent.UsuarioId);
    }

    [Fact]
    public async Task PublishUsuarioActualizadoAsync_PublishesEvent()
    {
        // Arrange
        var publisher = _provider.GetRequiredService<IEventPublisher>();
        var evento = new UsuarioActualizado(
            UsuarioId: Guid.NewGuid(),
            Rol: "Courier",
            Email: "rider@test.com",
            Telefono: "+57300987654",
            Activo: true,
            OccurredAt: DateTime.UtcNow
        );

        // Act
        await publisher.PublishUsuarioActualizadoAsync(evento);

        // Assert - Event published exactly once
        var published = _harness.Published.Select<UsuarioActualizado>().ToList();
        Assert.Single(published);

        var publishedEvent = published[0].Context.Message;
        Assert.Equal("Courier", publishedEvent.Rol);
        Assert.Equal("rider@test.com", publishedEvent.Email);
        Assert.Equal("+57300987654", publishedEvent.Telefono);
        Assert.True(publishedEvent.Activo);
        Assert.NotEqual(default, publishedEvent.UsuarioId);
    }

    [Fact]
    public async Task EventContent_ExcludesSensitiveData()
    {
        // Arrange
        var publisher = _provider.GetRequiredService<IEventPublisher>();
        var evento = new UsuarioRegistrado(
            UsuarioId: Guid.NewGuid(),
            Rol: "Restaurant",
            Email: "secure@test.com",
            Telefono: "+57300333333",
            Activo: true,
            OccurredAt: DateTime.UtcNow
        );

        // Act
        await publisher.PublishUsuarioRegistradoAsync(evento);

        // Assert - Event content doesn't contain sensitive data
        var published = _harness.Published.Select<UsuarioRegistrado>().ToList();
        Assert.Single(published);

        var publishedEvent = published[0].Context.Message;
        
        // Verify prohibited fields are NOT present
        var json = System.Text.Json.JsonSerializer.Serialize(publishedEvent);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jwt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hash", json, StringComparison.OrdinalIgnoreCase);
        
        // Verify required fields ARE present
        Assert.Contains("UsuarioId", json);
        Assert.Contains("Rol", json);
        Assert.Contains("Email", json);
        Assert.Contains("Telefono", json);
        Assert.Contains("Activo", json);
        Assert.Contains("OccurredAt", json);
    }
}