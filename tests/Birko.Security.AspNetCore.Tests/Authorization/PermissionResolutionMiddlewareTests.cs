using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authorization;

public class PermissionResolutionMiddlewareTests
{
    private static DefaultHttpContext AuthenticatedContext(Guid userId, Guid? tenantId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (tenantId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.TenantGuid, tenantId.Value.ToString()));
        }
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public async Task Authenticated_PopulatesBothPermissionAndRoleSlots()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var context = AuthenticatedContext(userId, tenantId);
        var resolver = new RecordingResolver
        {
            Permissions = new HashSet<string> { "users.read" },
            Roles = new HashSet<string> { "Admin" }
        };
        var nextCalled = false;
        var middleware = new PermissionResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, resolver, new ClaimMappingOptions());

        nextCalled.Should().BeTrue();
        resolver.GetAsyncCalls.Should().ContainSingle().Which.Should().Be((userId, tenantId));
        resolver.GetRolesAsyncCalls.Should().ContainSingle().Which.Should().Be((userId, tenantId));
        context.Items[PermissionResolutionMiddleware.ItemsKey].Should().BeEquivalentTo(new[] { "users.read" });
        context.Items[PermissionResolutionMiddleware.RolesItemsKey].Should().BeEquivalentTo(new[] { "Admin" });
    }

    [Fact]
    public async Task Authenticated_NoTenantClaim_PassesNullTenant()
    {
        var userId = Guid.NewGuid();
        var context = AuthenticatedContext(userId);
        var resolver = new RecordingResolver();
        var middleware = new PermissionResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, resolver, new ClaimMappingOptions());

        resolver.GetRolesAsyncCalls.Should().ContainSingle().Which.Should().Be((userId, (Guid?)null));
    }

    [Fact]
    public async Task Unauthenticated_NoOps_DoesNotResolveOrPopulateSlots()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var resolver = new RecordingResolver();
        var nextCalled = false;
        var middleware = new PermissionResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, resolver, new ClaimMappingOptions());

        nextCalled.Should().BeTrue();
        resolver.GetAsyncCalls.Should().BeEmpty();
        resolver.GetRolesAsyncCalls.Should().BeEmpty();
        context.Items.Should().NotContainKey(PermissionResolutionMiddleware.ItemsKey);
        context.Items.Should().NotContainKey(PermissionResolutionMiddleware.RolesItemsKey);
    }

    [Fact]
    public async Task Authenticated_InvalidUserIdClaim_NoOps()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var resolver = new RecordingResolver();
        var middleware = new PermissionResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, resolver, new ClaimMappingOptions());

        resolver.GetAsyncCalls.Should().BeEmpty();
        resolver.GetRolesAsyncCalls.Should().BeEmpty();
        context.Items.Should().NotContainKey(PermissionResolutionMiddleware.RolesItemsKey);
    }

    [Fact]
    public async Task DefaultGetRolesAsync_ReturnsEmpty()
    {
        // Resolvers that only implement GetAsync inherit the default GetRolesAsync (empty set),
        // so existing implementers keep compiling and ICurrentUser.Roles simply resolves to empty.
        IUserPermissionResolver resolver = new PermissionsOnlyResolver();

        var roles = await resolver.GetRolesAsync(Guid.NewGuid(), null);

        roles.Should().NotBeNull().And.BeEmpty();
    }

    private sealed class RecordingResolver : IUserPermissionResolver
    {
        public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();
        public IReadOnlySet<string> Roles { get; set; } = new HashSet<string>();
        public List<(Guid userId, Guid? tenantId)> GetAsyncCalls { get; } = new();
        public List<(Guid userId, Guid? tenantId)> GetRolesAsyncCalls { get; } = new();

        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        {
            GetAsyncCalls.Add((userId, tenantId));
            return Task.FromResult(Permissions);
        }

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
        {
            GetRolesAsyncCalls.Add((userId, tenantId));
            return Task.FromResult(Roles);
        }
    }

    // Only implements GetAsync — exercises the interface's default GetRolesAsync.
    private sealed class PermissionsOnlyResolver : IUserPermissionResolver
    {
        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}
