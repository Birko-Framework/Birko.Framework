using System.Security.Claims;
using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.User;

public class ClaimMappingOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new ClaimMappingOptions();

        options.UserIdClaim.Should().Be(ClaimTypes.NameIdentifier);
        options.EmailClaim.Should().Be(ClaimTypes.Email);
        options.TenantGuidClaim.Should().Be(JwtClaimNames.TenantGuid);
        options.RoleClaim.Should().Be(ClaimTypes.Role);
        options.PermissionClaim.Should().Be(JwtClaimNames.Permission);
    }

    [Fact]
    public void SetProperties_RoundTrips()
    {
        var options = new ClaimMappingOptions
        {
            UserIdClaim = "custom_uid",
            EmailClaim = "custom_email",
            TenantGuidClaim = "custom_tenant",
            RoleClaim = "custom_role",
            PermissionClaim = "custom_perm"
        };

        options.UserIdClaim.Should().Be("custom_uid");
        options.EmailClaim.Should().Be("custom_email");
        options.TenantGuidClaim.Should().Be("custom_tenant");
        options.RoleClaim.Should().Be("custom_role");
        options.PermissionClaim.Should().Be("custom_perm");
    }
}
