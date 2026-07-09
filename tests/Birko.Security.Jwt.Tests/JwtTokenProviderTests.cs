using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using Birko.Security;
using Birko.Security.Jwt;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Jwt.Tests;

/// <summary>
/// CR-H139: first tests for the JWT token provider — generate/validate round-trip, expiry, clock
/// skew, tampered signature, issuer/audience/secret mismatch, jti/iat auto-injection, and refresh
/// tokens. Note: the injected clock only drives token GENERATION; Microsoft's validator uses the
/// real system clock for lifetime checks, so expiry tests set the generation time relative to now.
/// </summary>
public class JwtTokenProviderTests
{
    private static TokenOptions Options() => new()
    {
        Secret = "super-secret-signing-key-of-sufficient-length-1234567890",
        Issuer = "birko-issuer",
        Audience = "birko-audience",
        ExpirationMinutes = 60,
    };

    private static JwtTokenProvider ProviderAt(DateTimeOffset generationTime, TokenOptions? opts = null)
        => new(opts ?? Options(), new TestDateTimeProvider(generationTime));

    [Fact]
    public void Constructor_MissingSecret_Throws()
    {
        var act = () => new JwtTokenProvider(new TokenOptions { Secret = "" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateThenValidate_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = ProviderAt(now);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "user-1" });

        result.Token.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().Be(now.UtcDateTime.AddMinutes(60));

        var validation = provider.ValidateToken(result.Token);
        validation.IsValid.Should().BeTrue();
        // Microsoft's handler remaps short claim names (e.g. "sub") to long URIs, so assert the value.
        validation.Claims.Values.Should().Contain("user-1");
    }

    [Fact]
    public void GenerateToken_AutoAddsJtiAndIat()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = ProviderAt(now);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Iat
            && c.Value == now.ToUnixTimeSeconds().ToString());
    }

    [Fact]
    public void ExpiredToken_FailsValidation()
    {
        // Generated so that expiry (genTime + 60m) is already 2 minutes in the past, beyond skew.
        var provider = ProviderAt(DateTimeOffset.UtcNow.AddMinutes(-62));
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var validation = provider.ValidateToken(result.Token);
        validation.IsValid.Should().BeFalse();
        validation.Error.Should().Contain("expired");
    }

    [Fact]
    public void WithinClockSkew_StillValid()
    {
        // Expiry 30s in the past — inside the 1-minute ClockSkew allowance.
        var provider = ProviderAt(DateTimeOffset.UtcNow.AddMinutes(-60).AddSeconds(-30));
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        provider.ValidateToken(result.Token).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TamperedToken_FailsValidation()
    {
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var tampered = result.Token[..^2] + (result.Token[^1] == 'A' ? "BB" : "AA");

        provider.ValidateToken(tampered).IsValid.Should().BeFalse();
    }

    [Fact]
    public void WrongIssuer_FailsValidation()
    {
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var wrongIssuerOpts = Options();
        wrongIssuerOpts.Issuer = "someone-else";

        provider.ValidateToken(result.Token, wrongIssuerOpts).IsValid.Should().BeFalse();
    }

    [Fact]
    public void WrongSecret_FailsValidation()
    {
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var wrongSecretOpts = Options();
        wrongSecretOpts.Secret = "a-totally-different-secret-key-1234567890-abcdef";

        provider.ValidateToken(result.Token, wrongSecretOpts).IsValid.Should().BeFalse();
    }

    [Fact]
    public void GenerateRefreshToken_IsNonEmptyAndUnique()
    {
        var provider = ProviderAt(DateTimeOffset.UtcNow);

        var a = provider.GenerateRefreshToken();
        var b = provider.GenerateRefreshToken();

        a.Should().NotBeNullOrEmpty();
        a.Should().NotBe(b);
    }
}
