using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

public class JwtAuthenticationOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new JwtAuthenticationOptions();

        options.Secret.Should().BeEmpty();
        options.Issuer.Should().Be("Birko");
        options.ExpirationMinutes.Should().Be(60);
        options.RefreshExpirationDays.Should().Be(7);
        options.ClockSkewSeconds.Should().Be(60);
        options.Claims.Should().NotBeNull();
    }

    [Fact]
    public void EffectiveAudience_WhenAudienceNull_ReturnsIssuer()
    {
        var options = new JwtAuthenticationOptions
        {
            Issuer = "test-issuer",
            Audience = null
        };

        options.EffectiveAudience.Should().Be("test-issuer");
    }

    [Fact]
    public void EffectiveAudience_WhenAudienceSet_ReturnsAudience()
    {
        var options = new JwtAuthenticationOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience"
        };

        options.EffectiveAudience.Should().Be("test-audience");
    }

    [Fact]
    public void SetProperties_RoundTrips()
    {
        var options = new JwtAuthenticationOptions
        {
            Secret = "my-secret",
            Issuer = "my-issuer",
            Audience = "my-audience",
            ExpirationMinutes = 120,
            RefreshExpirationDays = 14,
            ClockSkewSeconds = 30
        };

        options.Secret.Should().Be("my-secret");
        options.Issuer.Should().Be("my-issuer");
        options.Audience.Should().Be("my-audience");
        options.ExpirationMinutes.Should().Be(120);
        options.RefreshExpirationDays.Should().Be(14);
        options.ClockSkewSeconds.Should().Be(30);
    }
}
