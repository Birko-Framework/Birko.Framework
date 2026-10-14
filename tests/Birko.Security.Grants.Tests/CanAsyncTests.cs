using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class CanAsyncTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _resource = Guid.NewGuid();

    [Fact]
    public async Task ExactGrant_AllowsItsActions_OnThatResourceOnly()
    {
        await _f.SeedAsync(_user, Presentation, _resource, "editor");

        (await _f.Service.CanAsync(_user, Presentation, _resource, "edit")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Presentation, _resource, "delete")).Should().BeFalse();
        (await _f.Service.CanAsync(_user, Presentation, Guid.NewGuid(), "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task TypeWideGrant_AllowsEveryResourceOfThatType_AndNotOtherTypes()
    {
        await _f.SeedAsync(_user, Presentation, null, "editor");

        (await _f.Service.CanAsync(_user, Presentation, Guid.NewGuid(), "edit")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Presentation, null, "edit")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Draft, Guid.NewGuid(), "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task NullResourceId_MatchesOnlyTypeWideGrants()
    {
        await _f.SeedAsync(_user, Presentation, _resource, "owner");

        (await _f.Service.CanAsync(_user, Presentation, null, "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task ActionsUnion_AcrossTypeWideAndExactGrants()
    {
        await _f.SeedAsync(_user, Presentation, null, "viewer");
        await _f.SeedAsync(_user, Presentation, _resource, "editor");

        (await _f.Service.CanAsync(_user, Presentation, _resource, "edit")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Presentation, Guid.NewGuid(), "read")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Presentation, Guid.NewGuid(), "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task NoGrant_Denies()
    {
        (await _f.Service.CanAsync(_user, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task OtherUsersGrant_DoesNotApply()
    {
        await _f.SeedAsync(Guid.NewGuid(), Presentation, _resource, "owner");

        (await _f.Service.CanAsync(_user, Presentation, _resource, "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task AdminGrant_AllowsEveryActionOnEveryType()
    {
        await _f.SeedAdminAsync(_user);

        (await _f.Service.CanAsync(_user, Presentation, _resource, "delete")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Draft, null, "share")).Should().BeTrue();
    }

    [Fact]
    public async Task AdminSource_IsConsulted()
    {
        var service = new Services.GrantService(_f.Store, _f.Table, _f.Clock, new FixedAdminSource(_user));

        (await service.CanAsync(_user, Draft, _resource, "delete")).Should().BeTrue();
        (await service.CanAsync(Guid.NewGuid(), Draft, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task UndeclaredActionOrType_Refuses_EvenForAnAdmin()
    {
        await _f.SeedAdminAsync(_user);

        var badAction = () => _f.Service.CanAsync(_user, Presentation, _resource, "publish");
        var badType = () => _f.Service.CanAsync(_user, "invoice", _resource, "read");

        await badAction.Should().ThrowAsync<GrantConfigurationException>();
        await badType.Should().ThrowAsync<GrantConfigurationException>();
    }

    [Fact]
    public async Task StoredGrantWithUndeclaredRole_Refuses()
    {
        await _f.SeedAsync(_user, Presentation, _resource, "retired-role");

        var check = () => _f.Service.CanAsync(_user, Presentation, _resource, "read");

        await check.Should().ThrowAsync<GrantConfigurationException>().WithMessage("*retired-role*");
    }

    [Fact]
    public async Task Revoke_TakesEffectOnTheNextCheck()
    {
        var grant = await _f.SeedAsync(_user, Presentation, _resource, "editor");
        (await _f.Service.CanAsync(_user, Presentation, _resource, "edit")).Should().BeTrue();

        await _f.Store.DeleteAsync(grant);

        (await _f.Service.CanAsync(_user, Presentation, _resource, "edit")).Should().BeFalse();
    }

    private sealed class FixedAdminSource : Services.IGrantAdminSource
    {
        private readonly Guid _admin;
        public FixedAdminSource(Guid admin) => _admin = admin;
        public Task<bool> IsAdminAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default) => Task.FromResult(userGuid == _admin);
    }
}
