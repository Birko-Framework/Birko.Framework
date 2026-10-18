using System;
using Birko.Communication.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Birko.Communication.AspNetCore.Tests;

/// <summary>
/// TASK-541: <see cref="OwnerAccess"/>, the one owner-or-admin rule, and <see cref="OwnedCrudResults"/> deciding through it.
/// </summary>
public class OwnerAccessTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public void The_owner_may_access() => OwnerAccess.CanAccess(Alice, Alice).Should().BeTrue();

    [Fact]
    public void A_non_owner_may_not() => OwnerAccess.CanAccess(Bob, Alice).Should().BeFalse();

    [Fact]
    public void A_caller_who_sees_everything_may_access_anything()
    {
        OwnerAccess.CanAccess(Bob, Alice, seesEverything: true).Should().BeTrue();
        OwnerAccess.CanAccess((Guid?)null, null, seesEverything: true).Should().BeTrue();
        OwnerAccess.CanAccess("x", null, seesEverything: true).Should().BeTrue();
    }

    [Fact]
    public void A_null_caller_never_matches()
    {
        OwnerAccess.CanAccess(null, Alice).Should().BeFalse();
        OwnerAccess.CanAccess(null, "github:42").Should().BeFalse();
        OwnerAccess.CanAccess(string.Empty, string.Empty).Should().BeFalse("an empty identity is no identity");
    }

    [Fact]
    public void A_null_owner_never_matches()
    {
        OwnerAccess.CanAccess(Alice, null).Should().BeFalse();
        OwnerAccess.CanAccess((Guid?)null, null).Should().BeFalse("two missing identities are not the same owner");
        OwnerAccess.CanAccess((string?)null, null).Should().BeFalse();
    }

    [Fact]
    public void String_owners_compare_ordinally()
    {
        OwnerAccess.CanAccess("github:42", "github:42").Should().BeTrue();
        OwnerAccess.CanAccess("github:42", "GITHUB:42").Should().BeFalse();
    }

    private sealed class Doc
    {
        public Guid? Owner { get; init; }
    }

    [Fact]
    public void OwnedCrudResults_answers_a_non_owner_404_and_an_ownerless_entity_404()
    {
        OwnedCrudResults.ReadOwned(new Doc { Owner = Alice }, Bob, d => d.Owner, d => d).Should().BeOfType<NotFound>();
        OwnedCrudResults.ReadOwned(new Doc { Owner = null }, Alice, d => d.Owner, d => d).Should().BeOfType<NotFound>();
        OwnedCrudResults.ReadOwned(new Doc { Owner = Alice }, Alice, d => d.Owner, d => d).Should().BeOfType<Ok<object>>();
        OwnedCrudResults.RequireOwned<Doc>(new Doc { Owner = Bob }, Alice, d => d.Owner, out _).Should().BeOfType<NotFound>();
    }
}
