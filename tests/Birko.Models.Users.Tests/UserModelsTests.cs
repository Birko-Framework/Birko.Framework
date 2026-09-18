using System;
using Birko.Models.Users;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Users.Tests;

/// <summary>
/// CR-M226: the relational models' FK LoadFrom overloads did `XGuid = data.Guid!.Value`, which threw
/// InvalidOperationException for a transient (unsaved, Guid == null) view model — the `!` silenced the
/// compiler warning but converted it to a runtime crash. They now guard the nullable Guid.
/// CR-M228: GetDisplayName + the first test coverage for the project.
/// </summary>
public class UserModelsTests
{
    [Fact]
    public void UserRole_LoadFrom_TransientUser_DoesNotThrow()
    {
        var role = new UserRole();
        role.Invoking(r => r.LoadFrom(new ViewModels.User())) // Guid == null
            .Should().NotThrow();
        role.UserGuid.Should().Be(Guid.Empty, "a transient view model leaves the FK unset, not a crash");
    }

    [Fact]
    public void UserRole_LoadFrom_PersistedUser_SetsUserGuid()
    {
        var guid = Guid.NewGuid();
        var role = new UserRole();
        role.LoadFrom(new ViewModels.User { Guid = guid });
        role.UserGuid.Should().Be(guid);
    }

    [Fact]
    public void UserTenant_LoadFrom_TransientUserOrTenant_DoesNotThrow()
    {
        var ut = new UserTenant();
        ut.Invoking(x => x.LoadFrom(new ViewModels.User())).Should().NotThrow();
        ut.Invoking(x => x.LoadFrom(new ViewModels.Tenant())).Should().NotThrow();
    }

    [Fact]
    public void UserLogin_And_UserProfile_And_RolePermission_LoadFrom_Transient_DoNotThrow()
    {
        new UserLogin().Invoking(x => x.LoadFrom(new ViewModels.User())).Should().NotThrow();
        new UserProfile().Invoking(x => x.LoadFrom(new ViewModels.User())).Should().NotThrow();
        new RolePermission().Invoking(x => x.LoadFrom(new ViewModels.Role())).Should().NotThrow();
    }

    // ── CR-M228: GetDisplayName ──────────────────────────

    [Fact]
    public void GetDisplayName_PrefersExplicitDisplayName()
    {
        new UserProfile { DisplayName = "Admin", FirstName = "Jane", LastName = "Doe" }
            .GetDisplayName().Should().Be("Admin");
    }

    [Fact]
    public void GetDisplayName_FallsBackToFirstLast()
    {
        new UserProfile { FirstName = "Jane", LastName = "Doe" }.GetDisplayName().Should().Be("Jane Doe");
        new UserProfile { FirstName = "Jane" }.GetDisplayName().Should().Be("Jane");
        new UserProfile { LastName = "Doe" }.GetDisplayName().Should().Be("Doe");
    }

    [Fact]
    public void GetDisplayName_EmptyWhenNothingSet()
    {
        new UserProfile().GetDisplayName().Should().BeEmpty();
    }
}
