using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class TenantScopeTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _resource = Guid.NewGuid();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    [Fact]
    public async Task TenantBoundGrant_AppliesInItsTenantOnly()
    {
        await _f.SeedAsync(_user, Draft, _resource, "editor", _tenantA);

        (await _f.Service.CanAsync(_user, Draft, _resource, "edit", _tenantA)).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Draft, _resource, "edit", _tenantB)).Should().BeFalse();
    }

    [Fact]
    public async Task TenantBoundGrant_DoesNotApplyWithNoTenantInScope()
    {
        await _f.SeedAsync(_user, Draft, _resource, "editor", _tenantA);

        (await _f.Service.CanAsync(_user, Draft, _resource, "edit", tenantGuid: null)).Should().BeFalse();
    }

    [Fact]
    public async Task UntenantedGrant_AppliesInEveryTenant_AndWithNone()
    {
        await _f.SeedAsync(_user, Draft, _resource, "editor", tenant: null);

        (await _f.Service.CanAsync(_user, Draft, _resource, "edit", _tenantA)).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Draft, _resource, "edit", tenantGuid: null)).Should().BeTrue();
    }

    [Fact]
    public async Task TenantBoundAdmin_IsAdminInThatTenantOnly()
    {
        await _f.SeedAdminAsync(_user, _tenantA);

        (await _f.Service.CanAsync(_user, Draft, _resource, "delete", _tenantA)).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Draft, _resource, "delete", _tenantB)).Should().BeFalse();
    }

    [Fact]
    public async Task Grant_StampsTheTenantOfTheScope()
    {
        await _f.SeedAdminAsync(_user);

        var grant = await _f.Service.GrantAsync(_user, Guid.NewGuid(), Draft, _resource, "viewer", _tenantA);

        grant.TenantGuid.Should().Be(_tenantA);
    }

    [Fact]
    public async Task GrantInTenantA_DoesNotReplaceTheSameUsersGrantInTenantB()
    {
        await _f.SeedAdminAsync(_user);
        var other = Guid.NewGuid();
        await _f.Service.GrantAsync(_user, other, Draft, _resource, "viewer", _tenantA);

        await _f.Service.GrantAsync(_user, other, Draft, _resource, "editor", _tenantB);

        (await _f.AllAsync()).Where(g => g.UserGuid == other).Should().HaveCount(2);
    }

    [Fact]
    public async Task ShareInTenantA_DoesNotAuthorizeAGrantInTenantB()
    {
        await _f.SeedAsync(_user, Draft, _resource, "owner", _tenantA);

        var grant = () => _f.Service.GrantAsync(_user, Guid.NewGuid(), Draft, _resource, "viewer", _tenantB);

        (await grant.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task LastOwner_IsCountedPerTenant()
    {
        var owner = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(owner, Draft, _resource, _tenantA);
        await _f.SeedAsync(Guid.NewGuid(), Draft, _resource, "owner", _tenantB);

        var revoke = () => _f.Service.RevokeAsync(owner, owner, Draft, _resource, _tenantA);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastOwner);
    }

    [Fact]
    public async Task Listing_RespectsTenantScope()
    {
        var inA = Guid.NewGuid();
        await _f.SeedAsync(_user, Draft, inA, "viewer", _tenantA);
        await _f.SeedAsync(_user, Draft, Guid.NewGuid(), "viewer", _tenantB);

        var listed = await _f.Service.ListResourcesAsync(_user, Draft, "read", _tenantA);

        listed.Ids.Should().Equal(inA);
    }
}
