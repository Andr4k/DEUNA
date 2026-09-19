using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Shared.Middleware;
using Deuna.Shared.Extensions;

namespace Deuna.Shared.Tests;

public class JwtMiddlewareTests
{
    private readonly string _secretKey = "test-secret-key-at-least-32-characters-long-for-testing";
    private readonly string _issuer = "deuna-api";
    private readonly string _audience = "deuna-clients";
    private readonly Guid _testUserId = Guid.NewGuid();

    private JwtMiddleware CreateMiddleware(IConfiguration? config = null)
    {
        config ??= CreateConfiguration();
        return new JwtMiddleware(_ => Task.CompletedTask, config);
    }

    private IConfiguration CreateConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = _secretKey,
            ["Jwt:Issuer"] = _issuer,
            ["Jwt:Audience"] = _audience,
            ["Jwt:ValidateIssuer"] = "true",
            ["Jwt:ValidateAudience"] = "true"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
    }

    private DefaultHttpContext CreateContext(string? authHeader = null)
    {
        var context = new DefaultHttpContext();
        if (authHeader != null)
        {
            context.Request.Headers.Authorization = authHeader;
        }
        return context;
    }

    private string GenerateToken(Guid userId, string role, string email, DateTime? expires = null, string? customKey = null, string? customIssuer = null, string? customAudience = null)
    {
        var key = customKey ?? _secretKey;
        var issuer = customIssuer ?? _issuer;
        var audience = customAudience ?? _audience;

        var tokenHandler = new JwtSecurityTokenHandler();
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var now = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("sub", userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),
                new Claim(ClaimTypes.Email, email),
                new Claim("email", email)
            }),
            NotBefore = now.AddMinutes(-5), // Ensure NotBefore is before Expires
            Expires = expires ?? now.AddHours(8),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task InvokeAsync_WithValidToken_PopulatesUserIdAndRole()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "RESTAURANT", "test@restaurant.com");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(_testUserId, context.Items["UserId"]);
        Assert.Equal("RESTAURANT", context.Items["Role"]);
        Assert.Equal("test@restaurant.com", context.Items["Email"]);
        Assert.NotNull(context.User);
        Assert.True(context.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_WithValidToken_RiderRole_PopulatesCorrectly()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "RIDER", "rider@test.com");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(_testUserId, context.Items["UserId"]);
        Assert.Equal("RIDER", context.Items["Role"]);
        Assert.True(context.IsRider());
        Assert.False(context.IsRestaurant());
    }

    [Fact]
    public async Task InvokeAsync_WithValidToken_AdminRole_PopulatesCorrectly()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "ADMIN", "admin@test.com");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("ADMIN", context.Items["Role"]);
        Assert.True(context.IsAdmin());
    }

    [Fact]
    public async Task InvokeAsync_WithExpiredToken_SetsJwtError()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var expired = DateTime.UtcNow.AddMinutes(-1);
        var token = GenerateToken(_testUserId, "RESTAURANT", "test@test.com", expired);
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("Token expired", context.Items["JwtError"]);
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidSignature_SetsJwtError()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "RESTAURANT", "test@test.com", customKey: "different-secret-key-at-least-32-characters-long-for-testing");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("Invalid signature", context.Items["JwtError"]);
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_WithWrongIssuer_SetsJwtError()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "RESTAURANT", "test@test.com", customIssuer: "wrong-issuer");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("Invalid token", context.Items["JwtError"]);
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_WithWrongAudience_SetsJwtError()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var token = GenerateToken(_testUserId, "RESTAURANT", "test@test.com", customAudience: "wrong-audience");
        var context = CreateContext($"Bearer {token}");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("Invalid token", context.Items["JwtError"]);
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_WithoutToken_ContinuesWithoutUser()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Null(context.Items["UserId"]);
        Assert.Null(context.Items["Role"]);
        Assert.False(context.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_WithMalformedHeader_ContinuesWithoutUser()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext("InvalidHeader");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_WithBearerButNoToken_ContinuesWithoutUser()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext("Bearer ");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Null(context.Items["UserId"]);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new JwtMiddleware(next, CreateConfiguration());
        var context = CreateContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}
