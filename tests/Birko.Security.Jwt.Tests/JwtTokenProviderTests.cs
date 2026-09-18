using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
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
    public void GenerateToken_NullClaims_ThrowsArgumentNullException()
    {
        // CR-M238: fail fast like the ctor, not a bare NRE from the LINQ projection.
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        provider.Invoking(p => p.GenerateToken(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("claims");
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

    [Fact]
    public void GenerateToken_OverrideWithEmptySecret_Throws()
    {
        // CR-L343: the per-call override secret must be validated, not just _defaultOptions in the ctor.
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        var badOpts = Options();
        badOpts.Secret = "   ";

        provider.Invoking(p => p.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" }, badOpts))
            .Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void ValidateToken_OverrideWithEmptySecret_Throws()
    {
        // CR-L343: an empty override secret is a misconfiguration that must fail fast, not be swallowed
        // into a generic "unexpected error" validation failure.
        var provider = ProviderAt(DateTimeOffset.UtcNow);
        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });
        var badOpts = Options();
        badOpts.Secret = "";

        provider.Invoking(p => p.ValidateToken(result.Token, badOpts))
            .Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void GenerateToken_InconsistentClock_IatAndExpFromSameInstant()
    {
        // CR-L344: iat and exp are both derived from a single _clock.OffsetUtcNow read. A clock whose
        // UtcNow disagrees with OffsetUtcNow must not split the two claims across different instants —
        // exp minus iat must equal exactly ExpirationMinutes.
        var offsetInstant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new InconsistentClock(
            utcNow: offsetInstant.UtcDateTime.AddMinutes(30), // deliberately skewed vs OffsetUtcNow
            offsetUtcNow: offsetInstant);
        var provider = new JwtTokenProvider(Options(), clock);

        var result = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        var iat = long.Parse(jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Iat).Value);
        var exp = ((DateTimeOffset)jwt.ValidTo).ToUnixTimeSeconds();

        iat.Should().Be(offsetInstant.ToUnixTimeSeconds());
        (exp - iat).Should().Be(60 * 60); // ExpirationMinutes (60) in seconds — no cross-clock drift
    }

    private sealed class InconsistentClock : IDateTimeProvider
    {
        private readonly DateTime _utcNow;
        private readonly DateTimeOffset _offsetUtcNow;

        public InconsistentClock(DateTime utcNow, DateTimeOffset offsetUtcNow)
        {
            _utcNow = utcNow;
            _offsetUtcNow = offsetUtcNow;
        }

        public DateTime UtcNow => _utcNow;
        public DateTimeOffset OffsetUtcNow => _offsetUtcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow);
    }
}
