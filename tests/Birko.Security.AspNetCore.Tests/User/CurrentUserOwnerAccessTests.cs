using System;
using System.Collections.Generic;
using Birko.Security.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.User;

/// <summary>
/// TASK-541: the ICurrentUser form of the owner-or-admin rule, a layer over <c>OwnerAccess</c> in
/// Birko.Communication.AspNetCore (where it was placed by the user's decision), as DraCode's <c>ApiOwnership</c> used it.
/// </summary>
public class CurrentUserOwnerAccessTests
{
    private sealed class FakeUser(Guid? id, params string[] permissions) : ICurrentUser
    {
        public Guid? UserGuid { get; } = id;
        public string? Email => null;
        public Guid? TenantGuid => null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(permissions);
        public bool IsAuthenticated => UserGuid != null;
        public string? GetClaim(string claimType) => null;
    }

    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void The_owner_may_access_by_guid_and_by_string()
    {
        var user = new FakeUser(Owner);

        user.CanAccess(Owner).Should().BeTrue();
        user.CanAccess(Owner.ToString("D")).Should().BeTrue();
    }

    [Fact]
    public void Another_user_may_not()
    {
        var user = new FakeUser(Guid.NewGuid());

        user.CanAccess(Owner).Should().BeFalse();
        user.CanAccess(Owner.ToString("D")).Should().BeFalse();
    }

    [Fact]
    public void The_wildcard_or_the_named_view_all_permission_sees_everything()
    {
        new FakeUser(Guid.NewGuid(), "*").CanAccess(Owner).Should().BeTrue();
        new FakeUser(Guid.NewGuid(), "runs:view_all").CanAccess(Owner, "runs:view_all").Should().BeTrue();
        new FakeUser(Guid.NewGuid(), "runs:view_all").CanAccess(Owner).Should().BeFalse("without the name it is just a permission");
        new FakeUser(null, "*").SeesEverything().Should().BeTrue();
    }

    [Fact]
    public void A_missing_caller_or_owner_never_matches()
    {
        new FakeUser(null).CanAccess((Guid?)null).Should().BeFalse();
        new FakeUser(null).CanAccess((string?)null).Should().BeFalse();
        new FakeUser(Owner).CanAccess((Guid?)null).Should().BeFalse();
        new FakeUser(Owner).CanAccess("").Should().BeFalse();
    }
}
