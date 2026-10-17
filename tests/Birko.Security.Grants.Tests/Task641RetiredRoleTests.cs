using Birko.Security.Grants.Services;
using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

/// <summary>
/// TASK-641: a stored grant whose role was retired from the table grants nothing, so it can no longer make a user's
/// checks, listings or shares throw; <see cref="StaleGrantDetector"/> is where it is reported instead.
/// </summary>
public class Task641RetiredRoleTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();

    [Fact]
    public async Task A_retired_grant_allows_nothing_and_the_users_other_grants_still_count()
    {
        await _f.SeedAsync(_user, Presentation, _a, "retired-role");
        await _f.SeedAsync(_user, Presentation, null, "viewer");

        (await _f.Service.CanAsync(_user, Presentation, _a, "edit")).Should().BeFalse();
        (await _f.Service.CanAsync(_user, Presentation, _a, "read")).Should().BeTrue("the type-wide viewer grant still applies");
    }

    [Fact]
    public async Task A_type_wide_retired_grant_does_not_break_the_users_checks_of_the_type()
    {
        await _f.SeedAsync(_user, Presentation, null, "retired-role");
        await _f.SeedAsync(_user, Presentation, _b, "owner");

        (await _f.Service.CanAsync(_user, Presentation, _b, "edit")).Should().BeTrue();
        (await _f.Service.CanAsync(_user, Presentation, _a, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task Listing_leaves_the_retired_grant_out_and_keeps_the_rest()
    {
        await _f.SeedAsync(_user, Presentation, _a, "retired-role");
        await _f.SeedAsync(_user, Presentation, _b, "viewer");

        var listed = await _f.Service.ListResourcesAsync(_user, Presentation, "read");

        listed.AllOfType.Should().BeFalse();
        listed.Ids.Should().BeEquivalentTo(new[] { _b });
    }

    [Fact]
    public async Task An_owner_may_replace_or_remove_a_retired_grant_on_their_resource()
    {
        await _f.SeedAsync(_user, Presentation, _a, "owner");
        await _f.SeedAsync(_other, Presentation, _a, "retired-role");

        (await _f.Service.GrantAsync(_user, _other, Presentation, _a, "viewer")).Role.Should().Be("viewer");
        await _f.SeedAsync(Guid.NewGuid(), Presentation, _a, "retired-role");
        var stale = (await _f.AllAsync()).Single(g => g.Role == "retired-role");
        (await _f.Service.RevokeAsync(_user, stale.UserGuid, Presentation, _a)).Should().BeTrue();
    }

    [Fact]
    public async Task GrantAtLeast_over_a_retired_grant_writes_the_new_role()
    {
        await _f.SeedAsync(_user, Presentation, _a, "owner");
        await _f.SeedAsync(_other, Presentation, _a, "retired-role");

        (await _f.Service.GrantAtLeastAsync(_user, _other, Presentation, _a, "editor")).Role.Should().Be("editor");
    }

    [Fact]
    public async Task A_role_a_caller_passes_in_is_still_refused()
    {
        await _f.SeedAsync(_user, Presentation, _a, "owner");

        var grant = () => _f.Service.GrantAsync(_user, _other, Presentation, _a, "retired-role");

        await grant.Should().ThrowAsync<GrantConfigurationException>().WithMessage("*retired-role*");
    }

    [Fact]
    public async Task The_detector_lists_retired_roles_and_types_and_nothing_else()
    {
        await _f.SeedAdminAsync(_user);
        await _f.SeedAsync(_user, Presentation, _a, "owner");
        await _f.SeedAsync(_other, Presentation, _a, "retired-role");
        await _f.SeedAsync(_other, "retired-type", null, "viewer");

        var stale = await StaleGrantDetector.FindAsync(_f.Store, _f.Table);

        stale.Select(s => (s.Grant.Role, s.Grant.ResourceType, s.Reason)).Should().BeEquivalentTo(new[]
        {
            ("retired-role", Presentation, StaleGrantReason.RoleNotDeclared),
            ("viewer", "retired-type", StaleGrantReason.ResourceTypeNotDeclared),
        });
        stale.Select(s => s.ToString()).Should().Contain(s => s.Contains("undeclared role 'retired-role'"));
    }
}
