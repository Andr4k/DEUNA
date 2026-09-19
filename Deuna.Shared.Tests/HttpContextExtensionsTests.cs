using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Deuna.Shared.Extensions;

namespace Deuna.Shared.Tests;

public class HttpContextExtensionsTests
{
    private readonly Guid _testUserId = Guid.NewGuid();

    private DefaultHttpContext CreateContextWithItems(Guid? userId = null, string? role = null, string? email = null)
    {
        var context = new DefaultHttpContext();
        if (userId.HasValue) context.Items["UserId"] = userId.Value;
        if (role != null) context.Items["Role"] = role;
        if (email != null) context.Items["Email"] = email;
        return context;
    }

    private DefaultHttpContext CreateContextWithClaims(Guid? userId = null, string? role = null, string? email = null)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (userId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (email != null) claims.Add(new Claim(ClaimTypes.Email, email));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    [Fact]
    public void GetUserId_FromItems_ReturnsUserId()
    {
        var context = CreateContextWithItems(userId: _testUserId);
        Assert.Equal(_testUserId, context.GetUserId());
    }

    [Fact]
    public void GetUserId_FromClaims_ReturnsUserId()
    {
        var context = CreateContextWithClaims(userId: _testUserId);
        Assert.Equal(_testUserId, context.GetUserId());
    }

    [Fact]
    public void GetUserId_PrefersItemsOverClaims()
    {
        var itemsUserId = Guid.NewGuid();
        var claimsUserId = Guid.NewGuid();
        var context = CreateContextWithItems(userId: itemsUserId);
        context.User = CreateContextWithClaims(userId: claimsUserId).User;
        Assert.Equal(itemsUserId, context.GetUserId());
    }

    [Fact]
    public void GetUserId_NoUser_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        Assert.Null(context.GetUserId());
    }

    [Fact]
    public void GetRole_FromItems_ReturnsRole()
    {
        var context = CreateContextWithItems(role: "RESTAURANT");
        Assert.Equal("RESTAURANT", context.GetRole());
    }

    [Fact]
    public void GetRole_FromClaims_ReturnsRole()
    {
        var context = CreateContextWithClaims(role: "RIDER");
        Assert.Equal("RIDER", context.GetRole());
    }

    [Fact]
    public void GetEmail_FromItems_ReturnsEmail()
    {
        var context = CreateContextWithItems(email: "test@test.com");
        Assert.Equal("test@test.com", context.GetEmail());
    }

    [Fact]
    public void HasRole_WithMatchingRole_ReturnsTrue()
    {
        var context = CreateContextWithItems(role: "RESTAURANT");
        Assert.True(context.HasRole("RESTAURANT"));
        Assert.True(context.HasRole("restaurant")); // case insensitive
    }

    [Fact]
    public void HasRole_WithNonMatchingRole_ReturnsFalse()
    {
        var context = CreateContextWithItems(role: "RESTAURANT");
        Assert.False(context.HasRole("RIDER"));
    }

    [Fact]
    public void IsRestaurant_RestaurantRole_ReturnsTrue()
    {
        var context = CreateContextWithItems(role: "RESTAURANT");
        Assert.True(context.IsRestaurant());
        Assert.False(context.IsRider());
        Assert.False(context.IsAdmin());
    }

    [Fact]
    public void IsRider_RiderRole_ReturnsTrue()
    {
        var context = CreateContextWithItems(role: "RIDER");
        Assert.True(context.IsRider());
        Assert.False(context.IsRestaurant());
        Assert.False(context.IsAdmin());
    }

    [Fact]
    public void IsAdmin_AdminRole_ReturnsTrue()
    {
        var context = CreateContextWithItems(role: "ADMIN");
        Assert.True(context.IsAdmin());
        Assert.False(context.IsRestaurant());
        Assert.False(context.IsRider());
    }
}
