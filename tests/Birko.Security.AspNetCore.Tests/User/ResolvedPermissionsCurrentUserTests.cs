using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.User;

public class ResolvedPermissionsCurrentUserTests
{
    private static (ResolvedPermissionsCurrentUser user, HttpContext context) CreateUser(
        ClaimsPrincipal? principal,
        ClaimMappingOptions? options = null)
    {
        var httpContext = new DefaultHttpContext();
        if (principal != null)
        {
            httpContext.User = principal;
        }
        var accessor = new TestHttpContextAccessor { HttpContext = httpContext };
        var user = new ResolvedPermissionsCurrentUser(accessor, options ?? new ClaimMappingOptions());
        return (user, httpContext);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "TestAuth"));

    [Fact]
    public void Permissions_ReadsFromItemsSlot()
    {
        var (user, context) = CreateUser(AuthenticatedPrincipal());
        context.Items[PermissionResolutionMiddleware.ItemsKey] =
            (IReadOnlySet<string>)new HashSet<string> { "users.read", "users.write" };

        user.Permissions.Should().Contain("users.read").And.Contain("users.write");
    }

    [Fact]
    public void Permissions_NoSlot_ReturnsEmpty()
    {
        var (user, _) = CreateUser(AuthenticatedPrincipal());

        user.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Roles_ReadsResolvedSetFromItemsSlot()
    {
        var (user, context) = CreateUser(AuthenticatedPrincipal());
        context.Items[PermissionResolutionMiddleware.RolesItemsKey] =
            (IReadOnlySet<string>)new HashSet<string> { "Admin", "Manager" };

        user.Roles.Should().BeEquivalentTo("Admin", "Manager");
    }

    [Fact]
    public void Roles_ResolvedSetWins_OverRoleClaim()
    {
        // Even with a role claim present, the middleware-resolved set takes precedence.
        var (user, context) = CreateUser(AuthenticatedPrincipal(new Claim(ClaimTypes.Role, "FromClaim")));
        context.Items[PermissionResolutionMiddleware.RolesItemsKey] =
            (IReadOnlySet<string>)new HashSet<string> { "FromResolver" };

        user.Roles.Should().BeEquivalentTo("FromResolver");
    }

    [Fact]
    public void Roles_NoSlot_FallsBackToRoleClaim()
    {
        var (user, _) = CreateUser(AuthenticatedPrincipal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "User")));

        user.Roles.Should().BeEquivalentTo("Admin", "User");
    }

    [Theory]
    [InlineData("Admin,User,Guest")]
    [InlineData("Admin;User;Guest")]
    [InlineData("Admin, User; Guest")]
    public void Roles_FallbackSplitsOnBothDelimiters(string joined)
    {
        // Fallback path: a single role claim joined with ',' or ';' must split into discrete roles.
        var (user, _) = CreateUser(AuthenticatedPrincipal(new Claim(ClaimTypes.Role, joined)));

        user.Roles.Should().BeEquivalentTo("Admin", "User", "Guest");
    }

    [Fact]
    public void Roles_NoSlotNoClaim_ReturnsEmpty()
    {
        var (user, _) = CreateUser(AuthenticatedPrincipal());

        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void NullHttpContext_ReturnsEmptyRolesAndPermissions()
    {
        var accessor = new TestHttpContextAccessor { HttpContext = null };
        var user = new ResolvedPermissionsCurrentUser(accessor, new ClaimMappingOptions());

        user.Roles.Should().BeEmpty();
        user.Permissions.Should().BeEmpty();
    }

    private class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
