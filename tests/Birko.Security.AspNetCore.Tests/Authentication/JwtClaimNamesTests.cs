using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

public class JwtClaimNamesTests
{
    [Fact]
    public void UserId_IsSubClaim()
    {
        JwtClaimNames.UserId.Should().Be("sub");
    }

    [Fact]
    public void Email_IsEmailClaim()
    {
        JwtClaimNames.Email.Should().Be("email");
    }

    [Fact]
    public void TenantGuid_IsTenantIdClaim()
    {
        JwtClaimNames.TenantGuid.Should().Be("tenant_id");
    }

    [Fact]
    public void Permission_IsPermissionClaim()
    {
        JwtClaimNames.Permission.Should().Be("permission");
    }

    [Fact]
    public void Role_IsRoleClaim()
    {
        JwtClaimNames.Role.Should().Be("role");
    }
}
