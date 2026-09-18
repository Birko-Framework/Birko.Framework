using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Extensions;

public class BirkoSecurityOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new BirkoSecurityOptions();

        options.Jwt.Should().NotBeNull();
        options.TenantResolver.Should().Be(TenantResolverType.Header);
        options.SubdomainBaseDomain.Should().BeNull();
        options.SubdomainLookup.Should().BeNull();
        options.WildcardPermissionEnabled.Should().BeTrue();
    }

    [Fact]
    public void SetProperties_RoundTrips()
    {
        Func<string, CancellationToken, Task<TenantInfo?>> lookup =
            (_, _) => Task.FromResult<TenantInfo?>(null);

        var options = new BirkoSecurityOptions
        {
            TenantResolver = TenantResolverType.Subdomain,
            SubdomainBaseDomain = "example.com",
            SubdomainLookup = lookup,
            WildcardPermissionEnabled = false
        };

        options.TenantResolver.Should().Be(TenantResolverType.Subdomain);
        options.SubdomainBaseDomain.Should().Be("example.com");
        options.SubdomainLookup.Should().BeSameAs(lookup);
        options.WildcardPermissionEnabled.Should().BeFalse();
    }

    [Fact]
    public void Jwt_IsConfigurable()
    {
        var options = new BirkoSecurityOptions();
        options.Jwt.Secret = "test-secret";
        options.Jwt.Issuer = "test-issuer";

        options.Jwt.Secret.Should().Be("test-secret");
        options.Jwt.Issuer.Should().Be("test-issuer");
    }
}
