using Birko.Security.Grants.AspNetCore.Authorization;
using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests.AspNetCore;

public class PermissionResolverTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();

    private GrantAwareUserPermissionResolver Decorator(FixedPermissionResolver? host = null)
        => new(host ?? new FixedPermissionResolver(), _f.Store, _f.Table);

    [Fact]
    public async Task KeepsTheHostsPermissions_AndAddsTypeWideGrants()
    {
        var host = new FixedPermissionResolver().Give(_user, "billing:read");
        await _f.SeedAsync(_user, Presentation, null, "editor");

        var permissions = await Decorator(host).GetAsync(_user, null);

        permissions.Should().BeEquivalentTo("billing:read", "presentation:read", "presentation:edit");
    }

    [Fact]
    public async Task AdminGrant_BecomesTheWildcard()
    {
        await _f.SeedAdminAsync(_user);

        (await Decorator().GetAsync(_user, null)).Should().Contain("*");
    }

    [Fact]
    public async Task PerResourceGrants_AreNotSurfaced()
    {
        await _f.SeedAsync(_user, Presentation, Guid.NewGuid(), "owner");

        (await Decorator().GetAsync(_user, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task TypeWideGrants_AddRoles_AndRespectTenantScope()
    {
        var tenant = Guid.NewGuid();
        await _f.SeedAsync(_user, Draft, null, "viewer", tenant);

        (await Decorator().GetRolesAsync(_user, tenant)).Should().BeEquivalentTo("host-role", "draft:viewer");
        (await Decorator().GetRolesAsync(_user, null)).Should().BeEquivalentTo("host-role");
        (await Decorator().GetAsync(_user, Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task ATypeWideGrantWithARetiredRoleOrType_IsSkipped_NotThrown()
    {
        // The decorator runs in middleware on every request: throwing here would 500 every endpoint for this user.
        await _f.SeedAsync(_user, Presentation, null, "retired-role");
        await _f.SeedAsync(_user, "retired-type", null, "editor");
        await _f.SeedAsync(_user, Draft, null, "viewer");

        (await Decorator().GetAsync(_user, null)).Should().BeEquivalentTo("draft:read");
        (await Decorator().GetRolesAsync(_user, null)).Should().BeEquivalentTo("host-role", "draft:viewer");
    }

    [Fact]
    public void DecoratingTheDecorator_IsRefused()
    {
        var wrap = () => new GrantAwareUserPermissionResolver(Decorator(), _f.Store, _f.Table);

        wrap.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AdminSource_RefusesTheDecorator()
    {
        var build = () => new UserPermissionResolverAdminSource(Decorator(), _f.Store);

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AdminSource_AcceptsTheAdminGrant_OrTheHostsWildcard()
    {
        var hostAdmin = Guid.NewGuid();
        await _f.SeedAdminAsync(_user);
        var source = new UserPermissionResolverAdminSource(new FixedPermissionResolver().Give(hostAdmin, "*"), _f.Store);

        (await source.IsAdminAsync(_user, null)).Should().BeTrue();
        (await source.IsAdminAsync(hostAdmin, null)).Should().BeTrue();
        (await source.IsAdminAsync(Guid.NewGuid(), null)).Should().BeFalse();
    }
}
