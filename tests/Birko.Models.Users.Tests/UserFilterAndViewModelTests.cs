using System.Collections.Generic;
using Birko.Models.Users;
using FluentAssertions;
using Xunit;
using RoleModel = Birko.Models.Users.Role;
using RoleFilter = Birko.Models.Users.Filters.Role;
using RoleVm = Birko.Models.Users.ViewModels.Role;

namespace Birko.Models.Users.Tests;

/// <summary>
/// CR-L323: the per-class Combine wrapper was replaced by a shared FilterExpressions.Combine, and the
/// per-event new[]{...}.Contains dispatch by a cached HashSet. Both are behavior-preserving — these pin
/// the AND-composition and the aggregate object notification.
/// </summary>
public class UserFilterAndViewModelTests
{
    [Fact]
    public void RoleFilter_CombinesConditionsWithAnd()
    {
        var predicate = new RoleFilter { Name = "Admin", IsSystem = true }.Filter()!.Compile();

        predicate(new RoleModel { Name = "Admin", IsSystem = true }).Should().BeTrue();
        predicate(new RoleModel { Name = "Admin", IsSystem = false }).Should().BeFalse("both conditions must hold (AND)");
        predicate(new RoleModel { Name = "User", IsSystem = true }).Should().BeFalse();
    }

    [Fact]
    public void RoleViewModel_WatchedPropertyChange_RaisesRoleObjectNotification()
    {
        var vm = new RoleVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "Admin";

        raised.Should().Contain(RoleVm.NameProperty);
        raised.Should().Contain(RoleVm.RoleObjectProperty);
    }
}
