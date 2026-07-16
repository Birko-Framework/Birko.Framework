using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class TokenEndpointHandlerTests
{
    [Fact]
    public async Task ClientCredentials_IssuesAccessTokenWithoutRefresh()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials,
            ClientId = "c1",
            ClientSecret = "s1",
            Scope = "read"
        });

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(fixture.Settings.AccessTokenLifetimeSeconds);
        response.Scope.Should().Be("read");
        response.RefreshToken.Should().BeNull(); // RFC 6749 §4.4.3
    }

    [Fact]
    public async Task ClientCredentials_SupportedScopesConfigured_NarrowsToServerIntersection()
    {
        // CR-L349: OAuthServerSettings.SupportedScopes (previously dead) now constrains issuance. The client
        // is registered for {read, write} but the server only supports {read}, so "read write" narrows to "read".
        var fixture = new TestServer();
        fixture.Settings.SupportedScopes = new HashSet<string> { "read" };
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials,
            ClientId = "c1",
            ClientSecret = "s1",
            Scope = "read write"
        });

        response.Scope.Should().Be("read");
    }

    [Fact]
    public async Task ClientCredentials_SupportedScopesEmpty_ImposesNoConstraint()
    {
        // CR-L349: an empty SupportedScopes set must not constrain — the client's full allowed set is issued.
        var fixture = new TestServer(); // SupportedScopes defaults to empty
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials,
            ClientId = "c1",
            ClientSecret = "s1",
            Scope = "read write"
        });

        response.Scope.Should().Be("read write");
    }

    [Fact]
    public async Task ClientCredentials_RejectsWrongSecret()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials,
            ClientId = "c1",
            ClientSecret = "wrong"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidClient);
    }

    [Fact]
    public async Task ClientCredentials_RejectsUnauthorizedGrantType()
    {
        var fixture = new TestServer();
        // Client only allowed for authorization_code, but tries client_credentials
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials,
            ClientId = "c1",
            ClientSecret = "s1"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.UnauthorizedClient);
    }

    [Fact]
    public async Task AuthorizationCode_WithPkce_IssuesTokenPair()
    {
        var fixture = new TestServer();
        fixture.RegisterPublicClient("c1", OAuthGrantTypes.AuthorizationCode);

        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        await fixture.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "auth-code-1",
            ClientId = "c1",
            UserId = "user-42",
            RedirectUri = "https://app.test/callback",
            Scope = "read write",
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(1)
        });

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.AuthorizationCode,
            ClientId = "c1",
            Code = "auth-code-1",
            RedirectUri = "https://app.test/callback",
            CodeVerifier = verifier
        });

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        var stored = await fixture.Codes.GetByCodeAsync("auth-code-1");
        stored!.Used.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationCode_RejectsCodeReplay()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);
        await fixture.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "auth-code-1",
            ClientId = "c1",
            UserId = "u",
            RedirectUri = "https://app.test/callback",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(1),
            Used = true
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.AuthorizationCode,
            ClientId = "c1",
            ClientSecret = "s1",
            Code = "auth-code-1",
            RedirectUri = "https://app.test/callback"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }

    [Fact]
    public async Task AuthorizationCode_RejectsMismatchedRedirectUri()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);
        await fixture.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "auth-code-1",
            ClientId = "c1",
            UserId = "u",
            RedirectUri = "https://app.test/callback",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(1)
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.AuthorizationCode,
            ClientId = "c1",
            ClientSecret = "s1",
            Code = "auth-code-1",
            RedirectUri = "https://evil.test/callback"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }

    [Fact]
    public async Task AuthorizationCode_PublicClient_RejectsMissingPkce()
    {
        var fixture = new TestServer();
        fixture.RegisterPublicClient("c1", OAuthGrantTypes.AuthorizationCode);
        // Code stored with no challenge — public client must still send PKCE.
        await fixture.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "auth-code-1",
            ClientId = "c1",
            UserId = "u",
            RedirectUri = "https://app.test/callback",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(1)
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.AuthorizationCode,
            ClientId = "c1",
            Code = "auth-code-1",
            RedirectUri = "https://app.test/callback"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }

    [Fact]
    public async Task RefreshToken_Rotates_RevokesPreviousIssuesNew()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.RefreshToken);
        const string plaintext = "the-refresh-token";
        await fixture.Refreshes.CreateAsync(new RefreshTokenRecord
        {
            TokenHash = ClientSecretHasher.Hash(plaintext),
            ClientId = "c1",
            UserId = "user-1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddDays(1)
        });

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.RefreshToken,
            ClientId = "c1",
            ClientSecret = "s1",
            RefreshToken = plaintext
        });

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace().And.NotBe(plaintext);

        // Previous record revoked, new record present
        var oldHash = ClientSecretHasher.Hash(plaintext);
        var newHash = ClientSecretHasher.Hash(response.RefreshToken!);
        (await fixture.Refreshes.GetByHashAsync(oldHash))!.Revoked.Should().BeTrue();
        (await fixture.Refreshes.GetByHashAsync(newHash))!.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_RejectsExpired()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.RefreshToken);
        const string plaintext = "the-refresh-token";
        await fixture.Refreshes.CreateAsync(new RefreshTokenRecord
        {
            TokenHash = ClientSecretHasher.Hash(plaintext),
            ClientId = "c1",
            UserId = "user-1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddSeconds(-1)
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.RefreshToken,
            ClientId = "c1",
            ClientSecret = "s1",
            RefreshToken = plaintext
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }

    [Fact]
    public async Task DeviceCode_Pending_ReturnsAuthorizationPending()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        await fixture.Devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = "dev-code",
            UserCode = "ABCD-EFGH",
            ClientId = "c1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(10),
            Status = DeviceCodeStatus.Pending
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.DeviceCode,
            ClientId = "c1",
            ClientSecret = "s1",
            DeviceCode = "dev-code"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.AuthorizationPending);
    }

    [Fact]
    public async Task DeviceCode_PollingTooFast_ReturnsSlowDown()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        await fixture.Devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = "dev-code",
            UserCode = "ABCD-EFGH",
            ClientId = "c1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(10),
            Status = DeviceCodeStatus.Pending,
            LastPolledAt = fixture.Clock.UtcNow.AddSeconds(-1) // polled 1s ago, interval is 5s
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.DeviceCode,
            ClientId = "c1",
            ClientSecret = "s1",
            DeviceCode = "dev-code"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.SlowDown);
    }

    [Fact]
    public async Task DeviceCode_Authorized_IssuesTokenPair()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        await fixture.Devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = "dev-code",
            UserCode = "ABCD-EFGH",
            ClientId = "c1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(10),
            Status = DeviceCodeStatus.Authorized,
            UserId = "user-42"
        });

        var response = await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.DeviceCode,
            ClientId = "c1",
            ClientSecret = "s1",
            DeviceCode = "dev-code"
        });

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeviceCode_Denied_ReturnsAccessDenied()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        await fixture.Devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = "dev-code",
            UserCode = "ABCD-EFGH",
            ClientId = "c1",
            Scope = "read",
            ExpiresAt = fixture.Clock.UtcNow.AddMinutes(10),
            Status = DeviceCodeStatus.Denied
        });

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.DeviceCode,
            ClientId = "c1",
            ClientSecret = "s1",
            DeviceCode = "dev-code"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task UnsupportedGrantType_Throws()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var act = async () => await fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = "password", // resource-owner password — intentionally unsupported
            ClientId = "c1",
            ClientSecret = "s1"
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.UnsupportedGrantType);
    }
}
