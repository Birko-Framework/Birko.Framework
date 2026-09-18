using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authorization;

public class ClaimsPermissionCheckerTests
{
    [Fact]
    public async Task HasPermissionAsync_UserHasPermission_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUser(userId, permissions: new HashSet<string> { "users.read", "users.write" });
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.HasPermissionAsync(userId, "users.read");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_UserLacksPermission_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUser(userId, permissions: new HashSet<string> { "users.read" });
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.HasPermissionAsync(userId, "users.delete");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WildcardPermission_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUser(userId, permissions: new HashSet<string> { "*" });
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.HasPermissionAsync(userId, "anything.at.all");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_DifferentUserId_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var user = new TestCurrentUser(userId, permissions: new HashSet<string> { "users.read" });
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.HasPermissionAsync(otherUserId, "users.read");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetPermissionsAsync_MatchingUser_ReturnsPermissions()
    {
        var userId = Guid.NewGuid();
        var permissions = new HashSet<string> { "users.read", "users.write" };
        var user = new TestCurrentUser(userId, permissions: permissions);
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.GetPermissionsAsync(userId);

        result.Should().Contain("users.read").And.Contain("users.write");
    }

    [Fact]
    public async Task GetPermissionsAsync_DifferentUser_ReturnsEmpty()
    {
        var userId = Guid.NewGuid();
        var user = new TestCurrentUser(userId, permissions: new HashSet<string> { "users.read" });
        var checker = new ClaimsPermissionChecker(user);

        var result = await checker.GetPermissionsAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    private class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid? userId = null, string? email = null, Guid? tenantGuid = null,
            HashSet<string>? roles = null, HashSet<string>? permissions = null)
        {
            UserId = userId;
            Email = email;
            TenantGuid = tenantGuid;
            Roles = (IReadOnlySet<string>?)(roles?.AsReadOnly()) ?? new HashSet<string>().AsReadOnly();
            Permissions = (IReadOnlySet<string>?)(permissions?.AsReadOnly()) ?? new HashSet<string>().AsReadOnly();
        }

        public Guid? UserId { get; }
        public string? Email { get; }
        public Guid? TenantGuid { get; }
        public IReadOnlySet<string> Roles { get; }
        public IReadOnlySet<string> Permissions { get; }
        public bool IsAuthenticated => UserId.HasValue;
        public string? GetClaim(string claimType) => null;
    }
}
