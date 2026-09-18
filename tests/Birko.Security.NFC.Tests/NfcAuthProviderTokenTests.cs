using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Security;
using Birko.Security.NFC;
using FluentAssertions;
using Xunit;

namespace Birko.Security.NFC.Tests;

/// <summary>
/// CR-M239: AuthenticateAsync's JWT branch (IssueTokens + ITokenProvider present) was never exercised —
/// the tests never constructed the provider with an ITokenProvider. This covers token issuance, the
/// claim set, conditional email/name claims, and the no-token paths.
/// </summary>
public class NfcAuthProviderTokenTests
{
    private sealed class FakeTokenProvider : ITokenProvider
    {
        public IDictionary<string, string>? LastClaims { get; private set; }
        public int GenerateCalls { get; private set; }

        public TokenResult GenerateToken(IDictionary<string, string> claims, TokenOptions? options = null)
        {
            GenerateCalls++;
            LastClaims = claims;
            return new TokenResult { Token = "fake.jwt.token", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        }
        public string GenerateRefreshToken() => "refresh";
        public TokenValidationResult ValidateToken(string token, TokenOptions? options = null)
            => TokenValidationResult.Success(new Dictionary<string, string>());
    }

    private readonly InMemoryNfcTagMappingStore _store = new();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Authenticate_WithTokenProvider_IssuesTokenAndClaims()
    {
        var provider = new FakeTokenProvider();
        var settings = new NfcAuthSettings { IssueTokens = true };
        var auth = new NfcAuthProvider(_store, settings, provider);
        await auth.EnrollAsync(_userId, "AABBCC", userName: "Jane", email: "jane@test.com");

        var result = await auth.AuthenticateAsync("AABBCC");

        result.IsAuthenticated.Should().BeTrue();
        result.Token.Should().NotBeNull();
        result.Token!.Token.Should().Be("fake.jwt.token");
        provider.GenerateCalls.Should().Be(1);
        provider.LastClaims.Should().ContainKey("sub").WhoseValue.Should().Be(_userId.ToString());
        provider.LastClaims.Should().ContainKey("nfc_uid");
        provider.LastClaims.Should().ContainKey("auth_method").WhoseValue.Should().Be("nfc");
        provider.LastClaims.Should().ContainKey("email").WhoseValue.Should().Be("jane@test.com");
        provider.LastClaims.Should().ContainKey("name").WhoseValue.Should().Be("Jane");
    }

    [Fact]
    public async Task Authenticate_NoEmailOrName_OmitsThoseClaims()
    {
        var provider = new FakeTokenProvider();
        var auth = new NfcAuthProvider(_store, new NfcAuthSettings { IssueTokens = true }, provider);
        await auth.EnrollAsync(_userId, "AABBCC"); // no email/name

        await auth.AuthenticateAsync("AABBCC");

        provider.LastClaims.Should().NotContainKey("email");
        provider.LastClaims.Should().NotContainKey("name");
    }

    [Fact]
    public async Task Authenticate_IssueTokensFalse_NoToken()
    {
        var provider = new FakeTokenProvider();
        var auth = new NfcAuthProvider(_store, new NfcAuthSettings { IssueTokens = false }, provider);
        await auth.EnrollAsync(_userId, "AABBCC");

        var result = await auth.AuthenticateAsync("AABBCC");

        result.IsAuthenticated.Should().BeTrue();
        result.Token.Should().BeNull();
        provider.GenerateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Authenticate_NoTokenProvider_NoToken()
    {
        var auth = new NfcAuthProvider(_store, new NfcAuthSettings { IssueTokens = true }); // no provider
        await auth.EnrollAsync(_userId, "AABBCC");

        var result = await auth.AuthenticateAsync("AABBCC");

        result.IsAuthenticated.Should().BeTrue();
        result.Token.Should().BeNull();
    }
}
