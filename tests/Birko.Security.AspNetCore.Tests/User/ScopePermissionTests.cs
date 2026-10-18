using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.User;

/// <summary>
/// TASK-520: with <c>PermissionClaim = "scope"</c> (the OAuth scope claim, space-delimited by RFC 6749 §3.3) a
/// standard <c>"a b"</c> scope read as the one permission <c>"a b"</c>. It now reads as two; comma and semicolon joins
/// still work, and other permission claims are not split on spaces unless asked.
/// </summary>
public class ScopePermissionTests
{
    private static ClaimsCurrentUser User(string claimType, string value, ClaimMappingOptions options)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(claimType, value) }, "Test")),
        };
        return new ClaimsCurrentUser(new TestHttpContextAccessor { HttpContext = http }, options);
    }

    [Fact]
    public void A_space_delimited_scope_claim_reads_as_separate_permissions()
    {
        var user = User("scope", "projects:read  runs:write", new ClaimMappingOptions { PermissionClaim = "scope" });

        user.Permissions.Should().BeEquivalentTo(new[] { "projects:read", "runs:write" });
    }

    [Fact]
    public void A_comma_joined_scope_claim_still_works()
    {
        var user = User("scope", "projects:read,runs:write;*", new ClaimMappingOptions { PermissionClaim = "scope" });

        user.Permissions.Should().BeEquivalentTo(new[] { "projects:read", "runs:write", "*" });
    }

    [Fact]
    public void Another_permission_claim_is_not_split_on_spaces_by_default()
    {
        var user = User("permission", "reports read", new ClaimMappingOptions());

        user.Permissions.Should().BeEquivalentTo(new[] { "reports read" });
    }

    [Fact]
    public void Space_splitting_can_be_forced_on_or_off()
    {
        User("permission", "a b", new ClaimMappingOptions { SplitPermissionsOnSpace = true })
            .Permissions.Should().BeEquivalentTo(new[] { "a", "b" });
        User("scope", "a b", new ClaimMappingOptions { PermissionClaim = "scope", SplitPermissionsOnSpace = false })
            .Permissions.Should().BeEquivalentTo(new[] { "a b" });
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
