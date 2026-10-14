using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class GrantAndRevokeTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _resource = Guid.NewGuid();

    [Fact]
    public async Task Owner_GrantsEditor_WhoCanThenEdit()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");

        var grant = await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");

        grant.GrantedBy.Should().Be(_owner);
        grant.CreatedAt.Should().Be(_f.Clock.UtcNow);
        (await _f.Service.CanAsync(_other, Presentation, _resource, "edit")).Should().BeTrue();
    }

    [Fact]
    public async Task CallerWithoutShare_IsRefused()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "editor");

        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        (await grant.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task CallerWithNoGrant_IsRefused()
    {
        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        (await grant.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task SharerGrantingARoleAboveTheirOwn_IsEscalation()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "sharer");

        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");

        (await grant.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.Escalation);
        (await _f.AllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task SharerGrantingARoleWithinTheirOwn_IsAllowed()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "sharer");

        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        (await _f.Service.CanAsync(_other, Presentation, _resource, "read")).Should().BeTrue();
    }

    [Fact]
    public async Task SharerRevokingAnOwner_IsEscalation()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.SeedAsync(Guid.NewGuid(), Presentation, _resource, "owner");
        var sharer = Guid.NewGuid();
        await _f.SeedAsync(sharer, Presentation, _resource, "sharer");

        var revoke = () => _f.Service.RevokeAsync(sharer, _owner, Presentation, _resource);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.Escalation);
    }

    [Fact]
    public async Task SharerDemotingAnOwner_IsEscalation()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.SeedAsync(Guid.NewGuid(), Presentation, _resource, "owner");
        var sharer = Guid.NewGuid();
        await _f.SeedAsync(sharer, Presentation, _resource, "sharer");

        var demote = () => _f.Service.GrantAsync(sharer, _owner, Presentation, _resource, "viewer");

        (await demote.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.Escalation);
    }

    [Fact]
    public async Task TypeWideShare_LetsTheHolderGrantOnAnyResource()
    {
        await _f.SeedAsync(_owner, Presentation, null, "owner");

        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");

        (await _f.Service.CanAsync(_other, Presentation, _resource, "edit")).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_GrantsAnything()
    {
        await _f.SeedAdminAsync(_owner);

        await _f.Service.GrantAsync(_owner, _other, Draft, null, "owner");

        (await _f.Service.CanAsync(_other, Draft, Guid.NewGuid(), "delete")).Should().BeTrue();
    }

    [Fact]
    public async Task Regrant_ReplacesTheRole_NeverDuplicates()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");
        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");

        var others = (await _f.AllAsync()).Where(g => g.UserGuid == _other).ToList();
        others.Should().ContainSingle().Which.Role.Should().Be("editor");
    }

    [Fact]
    public async Task UndeclaredRole_Refuses()
    {
        await _f.SeedAdminAsync(_owner);

        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "superuser");
        var reserved = () => _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "*");

        await grant.Should().ThrowAsync<GrantConfigurationException>();
        await reserved.Should().ThrowAsync<GrantConfigurationException>();
    }

    [Fact]
    public async Task EmptyResourceId_IsRefused_NotTreatedAsTypeWide()
    {
        await _f.SeedAdminAsync(_owner);

        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, Guid.Empty, "viewer");

        await grant.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Revoke_RemovesAccess_AndReportsWhetherThereWasAGrant()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "editor");

        (await _f.Service.RevokeAsync(_owner, _other, Presentation, _resource)).Should().BeTrue();
        (await _f.Service.RevokeAsync(_owner, _other, Presentation, _resource)).Should().BeFalse();
        (await _f.Service.CanAsync(_other, Presentation, _resource, "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_ByAnUnauthorizedCaller_IsRefused()
    {
        await _f.SeedAsync(_other, Presentation, _resource, "viewer");

        var revoke = () => _f.Service.RevokeAsync(Guid.NewGuid(), _other, Presentation, _resource);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task AnyoneMayGiveUpTheirOwnAccess()
    {
        await _f.SeedAsync(_other, Presentation, _resource, "viewer");

        (await _f.Service.RevokeAsync(_other, _other, Presentation, _resource)).Should().BeTrue();
    }

    [Fact]
    public async Task OwnerOnCreate_MakesTheCreatorOwner_Idempotently()
    {
        var first = await _f.Service.GrantOwnerOnCreateAsync(_owner, Draft, _resource);
        var second = await _f.Service.GrantOwnerOnCreateAsync(_owner, Draft, _resource);

        first.Role.Should().Be("owner");
        first.GrantedBy.Should().BeNull();
        second.Guid.Should().Be(first.Guid);
        (await _f.AllAsync()).Should().ContainSingle();
        (await _f.Service.CanAsync(_owner, Draft, _resource, "share")).Should().BeTrue();
    }

    [Fact]
    public async Task OwnerOnCreate_OnAResourceSomeoneElseOwns_IsRefused()
    {
        await _f.SeedAsync(_other, Draft, _resource, "owner");
        await _f.SeedAsync(_owner, Draft, _resource, "viewer");

        var claim = () => _f.Service.GrantOwnerOnCreateAsync(_owner, Draft, _resource);

        (await claim.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
        (await _f.Service.CanAsync(_owner, Draft, _resource, "share")).Should().BeFalse();
    }

    [Fact]
    public async Task OwnerOnCreate_OnAnUnownedResource_PromotesAnExistingLesserRole()
    {
        await _f.SeedAsync(_owner, Draft, _resource, "viewer");

        var grant = await _f.Service.GrantOwnerOnCreateAsync(_owner, Draft, _resource);

        grant.Role.Should().Be("owner");
        (await _f.AllAsync()).Should().ContainSingle().Which.Role.Should().Be("owner");
    }

    [Fact]
    public async Task ExactOwner_MayNotGrantTypeWide()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");

        var grant = () => _f.Service.GrantAsync(_owner, _other, Presentation, null, "viewer");

        (await grant.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task ReservedAdminType_CannotBeWrittenThroughTheService()
    {
        await _f.SeedAdminAsync(_owner);

        var grant = () => _f.Service.GrantAsync(_owner, _other, "*", null, "owner");
        var claim = () => _f.Service.GrantOwnerOnCreateAsync(_other, "*", _resource);

        await grant.Should().ThrowAsync<GrantConfigurationException>();
        await claim.Should().ThrowAsync<GrantConfigurationException>();
    }

    [Fact]
    public async Task Admin_MayRevokeAGrantWhoseRoleWasRetired()
    {
        await _f.SeedAdminAsync(_owner);
        await _f.SeedAsync(_other, Presentation, _resource, "retired-role");

        (await _f.Service.RevokeAsync(_owner, _other, Presentation, _resource)).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_MayReplaceAGrantWhoseRoleWasRetired()
    {
        await _f.SeedAdminAsync(_owner);
        await _f.SeedAsync(_other, Presentation, _resource, "retired-role");

        var grant = await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        grant.Role.Should().Be("viewer");
    }

    [Fact]
    public async Task Revoke_RemovesEveryRowWithTheKey_SoADuplicateCannotKeepAccess()
    {
        // Two rows for one key: what two instances granting at once leave behind on a backend with no unique index.
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.SeedAsync(_other, Presentation, _resource, "editor");
        await _f.SeedAsync(_other, Presentation, _resource, "editor");

        (await _f.Service.RevokeAsync(_owner, _other, Presentation, _resource)).Should().BeTrue();

        (await _f.Service.CanAsync(_other, Presentation, _resource, "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task Regrant_UpdatesEveryRowWithTheKey()
    {
        await _f.SeedAsync(_owner, Presentation, _resource, "owner");
        await _f.SeedAsync(_other, Presentation, _resource, "editor");
        await _f.SeedAsync(_other, Presentation, _resource, "editor");

        await _f.Service.GrantAsync(_owner, _other, Presentation, _resource, "viewer");

        (await _f.Service.CanAsync(_other, Presentation, _resource, "edit")).Should().BeFalse();
    }

    [Fact]
    public async Task OwnerOnCreate_EmptyId_Refuses()
    {
        var create = () => _f.Service.GrantOwnerOnCreateAsync(_owner, Draft, Guid.Empty);

        await create.Should().ThrowAsync<ArgumentException>();
    }
}
