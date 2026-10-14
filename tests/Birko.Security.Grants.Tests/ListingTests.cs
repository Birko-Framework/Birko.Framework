using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class ListingTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task ListResources_ReturnsExactIds_AndNotOtherTypes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await _f.SeedAsync(_user, Presentation, a, "owner");
        await _f.SeedAsync(_user, Presentation, b, "viewer");
        await _f.SeedAsync(_user, Draft, Guid.NewGuid(), "owner");

        var listed = await _f.Service.ListResourcesAsync(_user, Presentation, "read");

        listed.AllOfType.Should().BeFalse();
        listed.Ids.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public async Task ListResources_TypeWideGrant_IsNotLost()
    {
        var a = Guid.NewGuid();
        await _f.SeedAsync(_user, Presentation, null, "viewer");
        await _f.SeedAsync(_user, Presentation, a, "owner");

        var listed = await _f.Service.ListResourcesAsync(_user, Presentation, "read");

        listed.AllOfType.Should().BeTrue();
        listed.Ids.Should().Equal(a);
    }

    [Fact]
    public async Task ListResources_IgnoresGrantsWhoseRoleDoesNotAllowTheAction()
    {
        var viewable = Guid.NewGuid();
        await _f.SeedAsync(_user, Presentation, null, "viewer");
        await _f.SeedAsync(_user, Presentation, viewable, "viewer");

        var editable = await _f.Service.ListResourcesAsync(_user, Presentation, "edit");

        // A type-wide viewer grant must not make every presentation editable-listed.
        editable.AllOfType.Should().BeFalse();
        editable.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ListResources_UndeclaredAction_Refuses()
    {
        var list = () => _f.Service.ListResourcesAsync(_user, Presentation, "publish");

        await list.Should().ThrowAsync<GrantConfigurationException>();
    }

    [Fact]
    public async Task ListResources_Admin_IsAllOfType()
    {
        await _f.SeedAdminAsync(_user);

        (await _f.Service.ListResourcesAsync(_user, Draft, "read")).AllOfType.Should().BeTrue();
    }

    [Fact]
    public async Task ListGrantees_ReturnsExactAndTypeWideGrants_Flagged()
    {
        var resource = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var typeWide = Guid.NewGuid();
        await _f.SeedAsync(owner, Presentation, resource, "owner");
        await _f.SeedAsync(typeWide, Presentation, null, "editor");
        await _f.SeedAsync(Guid.NewGuid(), Presentation, Guid.NewGuid(), "owner");

        var grantees = await _f.Service.ListGranteesAsync(Presentation, resource);

        grantees.Should().HaveCount(2);
        grantees.Single(e => e.Grant.UserGuid == owner).IsTypeWide.Should().BeFalse();
        grantees.Single(e => e.Grant.UserGuid == typeWide).IsTypeWide.Should().BeTrue();
    }
}
