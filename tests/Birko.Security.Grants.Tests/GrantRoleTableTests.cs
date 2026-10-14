using FluentAssertions;
using Xunit;

namespace Birko.Security.Grants.Tests;

public class GrantRoleTableTests
{
    private static GrantOptions Valid() => new GrantOptions()
        .DefineRole("owner", "read", "share")
        .ResourceType("doc");

    [Fact]
    public void Build_ValidOptions_ExposesRolesActionsAndVisibility()
    {
        var table = Valid().DefineRole("viewer", "read").ResourceType("page", ResourceVisibility.Public).Build();

        table.ActionsOf("owner").Should().BeEquivalentTo("read", "share");
        table.Allows("viewer", "read").Should().BeTrue();
        table.Allows("viewer", "share").Should().BeFalse();
        table.VisibilityOf("doc").Should().Be(ResourceVisibility.Granted);
        table.VisibilityOf("page").Should().Be(ResourceVisibility.Public);
    }

    public static TheoryData<string, Func<GrantOptions>> InvalidOptions => new()
    {
        { "empty role name", () => Valid().DefineRole(" ", "read") },
        { "duplicate role", () => Valid().DefineRole("owner", "read") },
        { "role with no actions", () => Valid().DefineRole("nobody") },
        { "empty action", () => Valid().DefineRole("viewer", "") },
        { "reserved role", () => Valid().DefineRole("*", "read") },
        { "reserved action", () => Valid().DefineRole("viewer", "*") },
        { "reserved resource type", () => Valid().ResourceType("*") },
        { "duplicate resource type", () => Valid().ResourceType("doc") },
        { "undefined visibility", () => Valid().ResourceType("page", (ResourceVisibility)42) },
        { "no resource type", () => new GrantOptions().DefineRole("owner", "share") },
        { "owner role undeclared", () => new GrantOptions().DefineRole("admin", "share").ResourceType("doc") },
        { "owner role cannot share", () => new GrantOptions().DefineRole("owner", "read").DefineRole("x", "share").ResourceType("doc") },
        { "share action undeclared", () => new GrantOptions().DefineRole("owner", "read").ResourceType("doc") },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Build_InvalidOptions_Refuses(string because, Func<GrantOptions> options)
    {
        var build = () => options().Build();

        build.Should().Throw<GrantConfigurationException>(because);
    }

    [Fact]
    public void Build_CustomOwnerAndShareNames_AreHonoured()
    {
        var options = new GrantOptions { OwnerRole = "admin", ShareAction = "invite" }
            .DefineRole("admin", "invite", "edit")
            .ResourceType("doc");

        var table = options.Build();

        table.OwnerRole.Should().Be("admin");
        table.ShareAction.Should().Be("invite");
    }

    [Fact]
    public void Lookups_OfUndeclaredNames_RefuseRatherThanAnswerNo()
    {
        var table = Valid().Build();

        ((Action)(() => table.ActionsOf("editor"))).Should().Throw<GrantConfigurationException>();
        ((Action)(() => table.Allows("owner", "edit"))).Should().Throw<GrantConfigurationException>();
        ((Action)(() => table.VisibilityOf("page"))).Should().Throw<GrantConfigurationException>();
        ((Action)(() => table.RequireAction("*"))).Should().Throw<GrantConfigurationException>();
    }

    [Fact]
    public void Table_IsImmutable_AfterBuild()
    {
        var options = Valid();
        var table = options.Build();

        options.DefineRole("editor", "read");

        table.Roles.Should().BeEquivalentTo("owner");
    }
}
