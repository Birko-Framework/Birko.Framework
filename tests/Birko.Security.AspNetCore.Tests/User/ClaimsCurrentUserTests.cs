using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.User;

public class ClaimsCurrentUserTests
{
    private static ClaimsCurrentUser CreateUser(ClaimsPrincipal? principal, ClaimMappingOptions? options = null)
    {
        var httpContext = new DefaultHttpContext();
        if (principal != null)
        {
            httpContext.User = principal;
        }
        var accessor = new TestHttpContextAccessor { HttpContext = httpContext };
        return new ClaimsCurrentUser(accessor, options ?? new ClaimMappingOptions());
    }

    [Fact]
    public void Authenticated_ReturnsAllProperties()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, "user@example.com"),
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(ClaimTypes.Role, "Admin"),
            new(ClaimTypes.Role, "User"),
            new(JwtClaimNames.Permission, "users.read"),
            new(JwtClaimNames.Permission, "users.write")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal);

        user.IsAuthenticated.Should().BeTrue();
        user.UserId.Should().Be(userId);
        user.Email.Should().Be("user@example.com");
        user.TenantId.Should().Be(tenantId);
        user.Roles.Should().Contain("Admin").And.Contain("User");
        user.Permissions.Should().Contain("users.read").And.Contain("users.write");
    }

    [Fact]
    public void Unauthenticated_ReturnsDefaults()
    {
        var identity = new ClaimsIdentity(); // no auth type = unauthenticated
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal);

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.Email.Should().BeNull();
        user.TenantId.Should().BeNull();
        user.Roles.Should().BeEmpty();
        user.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void NullHttpContext_ReturnsDefaults()
    {
        var accessor = new TestHttpContextAccessor { HttpContext = null };
        var user = new ClaimsCurrentUser(accessor, new ClaimMappingOptions());

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
    }

    [Fact]
    public void GetClaim_ExistingClaim_ReturnsValue()
    {
        var claims = new List<Claim> { new("custom_claim", "custom_value") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal);

        user.GetClaim("custom_claim").Should().Be("custom_value");
    }

    [Fact]
    public void GetClaim_MissingClaim_ReturnsNull()
    {
        var identity = new ClaimsIdentity([], "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal);

        user.GetClaim("nonexistent").Should().BeNull();
    }

    [Fact]
    public void CustomClaimMapping_UsesConfiguredClaims()
    {
        var userId = Guid.NewGuid();
        var options = new ClaimMappingOptions
        {
            UserIdClaim = "uid",
            EmailClaim = "mail"
        };
        var claims = new List<Claim>
        {
            new("uid", userId.ToString()),
            new("mail", "custom@example.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal, options);

        user.UserId.Should().Be(userId);
        user.Email.Should().Be("custom@example.com");
    }

    [Fact]
    public void InvalidGuid_ForUserId_ReturnsNull()
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "not-a-guid") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var user = CreateUser(principal);

        user.UserId.Should().BeNull();
    }

    private class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
