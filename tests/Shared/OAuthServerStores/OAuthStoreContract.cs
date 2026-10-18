using Birko.Security;
using Birko.Security.Jwt;
using Birko.Security.OAuth.Server;
using Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;
using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.StoreContract;

/// <summary>The five OAuth server stores under test.</summary>
public sealed record OAuthStores(
    IOAuthClientStore Clients,
    IAuthorizationCodeStore Codes,
    IRefreshTokenStore Refreshes,
    IDeviceCodeStore Devices,
    IConsentStore Consents);

/// <summary>
/// TASK-518: what every shipped OAuth store set must do — round-trip each model (an OAuth client's collections and a
/// refresh record's claims included), revoke a family, claim a code or a refresh token exactly once under concurrency,
/// and carry the whole <see cref="OAuthServer"/> through its grants. Linked into each store package's test project.
/// </summary>
public abstract class OAuthStoreContract
{
    protected TestDateTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    /// <summary>A fresh, empty store set.</summary>
    protected abstract OAuthStores NewStores();

    private const string Secret = "contract-secret-contract-secret-contract-secret";

    protected OAuthServer NewServer(OAuthStores stores, bool rotate = true)
    {
        var settings = new OAuthServerSettings
        {
            Location = "https://auth.test/",
            Issuer = "https://auth.test/",
            AccessTokenLifetimeSeconds = 3600,
            RefreshTokenLifetimeSeconds = 60 * 60 * 24,
            AuthorizationCodeLifetimeSeconds = 60,
            DeviceCodeLifetimeSeconds = 600,
            DeviceCodePollingIntervalSeconds = 5,
            RotateRefreshTokens = rotate,
            RequirePkceForPublicClients = true,
        };
        var tokenOptions = new TokenOptions { Secret = Secret, Issuer = "https://auth.test/", Audience = "https://api.test/", ExpirationMinutes = 60 };
        return new OAuthServer(settings, new JwtTokenProvider(tokenOptions, Clock), tokenOptions,
            stores.Clients, stores.Codes, stores.Refreshes, stores.Devices, stores.Consents,
            deviceVerificationUri: "https://auth.test/device", clock: Clock);
    }

    protected static OAuthClient NewPublicClient(string clientId, params string[] grants) => new()
    {
        ClientId = clientId,
        ClientType = OAuthClientType.Public,
        Name = clientId,
        RedirectUris = { "https://app.test/callback", "https://app.test/other" },
        AllowedGrantTypes = grants.ToList(),
        AllowedScopes = { "read", "write" },
        IsEnabled = true,
    };

    private static IDictionary<string, string> PayloadClaims(string accessToken) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => string.Join(";", g.Select(c => c.Value)));

    // ---- round-trips -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_client_round_trips_with_its_collections()
    {
        var stores = NewStores();
        var client = NewPublicClient("web", OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken);
        client.ClientSecretHash = "hash";
        await stores.Clients.CreateAsync(client);

        var read = await stores.Clients.GetByClientIdAsync("web");

        read.Should().NotBeNull();
        read!.Guid.Should().Be(client.Guid);
        read.ClientSecretHash.Should().Be("hash");
        read.ClientType.Should().Be(OAuthClientType.Public);
        read.RedirectUris.Should().Equal("https://app.test/callback", "https://app.test/other");
        read.AllowedGrantTypes.Should().Equal(OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken);
        read.AllowedScopes.Should().Equal("read", "write");
        (await stores.Clients.GetByClientIdAsync("nobody")).Should().BeNull();
    }

    [Fact]
    public async Task A_client_updates_and_deletes()
    {
        var stores = NewStores();
        var client = NewPublicClient("web", OAuthGrantTypes.RefreshToken);
        await stores.Clients.CreateAsync(client);

        client.Name = "renamed";
        client.AllowedScopes = new List<string> { "read" };
        await stores.Clients.UpdateAsync(client);
        var read = await stores.Clients.GetByClientIdAsync("web");
        read!.Name.Should().Be("renamed");
        read.AllowedScopes.Should().Equal("read");

        await stores.Clients.DeleteAsync(read);
        (await stores.Clients.GetByClientIdAsync("web")).Should().BeNull();
    }

    [Fact]
    public async Task A_refresh_record_round_trips_with_its_claims()
    {
        var stores = NewStores();
        var record = new RefreshTokenRecord
        {
            TokenHash = "H1", ClientId = "web", UserId = "github:42", Scope = "read",
            ExpiresAt = Clock.UtcNow.AddDays(1),
            Claims = new Dictionary<string, string> { ["name"] = "octocat", ["roles"] = "admin,user" },
        };
        await stores.Refreshes.CreateAsync(record);

        var read = await stores.Refreshes.GetByHashAsync("H1");

        read.Should().NotBeNull();
        read!.UserId.Should().Be("github:42");
        read.ExpiresAt.Should().Be(record.ExpiresAt);
        read.Revoked.Should().BeFalse();
        read.Claims.Should().BeEquivalentTo(record.Claims);
        (await stores.Refreshes.GetByHashAsync("H2")).Should().BeNull();
    }

    [Fact]
    public async Task A_code_a_device_record_and_a_consent_round_trip()
    {
        var stores = NewStores();
        await stores.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "c1", ClientId = "web", UserId = "u", RedirectUri = "https://app.test/callback", Scope = "read",
            CodeChallenge = "ch", CodeChallengeMethod = "S256", ExpiresAt = Clock.UtcNow.AddMinutes(1),
        });
        await stores.Devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = "d1", UserCode = "ABCD-EFGH", ClientId = "web", Scope = "read",
            ExpiresAt = Clock.UtcNow.AddMinutes(10), Status = DeviceCodeStatus.Authorized, UserId = "u",
        });
        await stores.Consents.CreateAsync(new ConsentRecord { UserId = "u", ClientId = "web", Scope = "read write" });

        var code = await stores.Codes.GetByCodeAsync("c1");
        code!.CodeChallenge.Should().Be("ch");
        code.Used.Should().BeFalse();
        var device = await stores.Devices.GetByUserCodeAsync("ABCD-EFGH");
        device!.DeviceCode.Should().Be("d1");
        device.Status.Should().Be(DeviceCodeStatus.Authorized);
        (await stores.Devices.GetByDeviceCodeAsync("d1"))!.UserId.Should().Be("u");
        (await stores.Consents.GetAsync("u", "web"))!.Scope.Should().Be("read write");
        (await stores.Consents.GetAsync("u", "other")).Should().BeNull();
    }

    // ---- revoke and claims ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Revoking_a_family_revokes_only_that_family()
    {
        var stores = NewStores();
        foreach (var (hash, client, user) in new[] { ("a", "web", "u1"), ("b", "web", "u1"), ("c", "web", "u2"), ("d", "cli", "u1") })
        {
            await stores.Refreshes.CreateAsync(new RefreshTokenRecord
            {
                TokenHash = hash, ClientId = client, UserId = user, ExpiresAt = Clock.UtcNow.AddDays(1),
            });
        }

        await stores.Refreshes.RevokeFamilyAsync("web", "u1");

        (await stores.Refreshes.GetByHashAsync("a"))!.Revoked.Should().BeTrue();
        (await stores.Refreshes.GetByHashAsync("b"))!.Revoked.Should().BeTrue();
        (await stores.Refreshes.GetByHashAsync("c"))!.Revoked.Should().BeFalse();
        (await stores.Refreshes.GetByHashAsync("d"))!.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task Of_concurrent_redemptions_of_one_code_exactly_one_succeeds()
    {
        var stores = NewStores();
        var code = new AuthorizationCode { Code = "c1", ClientId = "web", UserId = "u", ExpiresAt = Clock.UtcNow.AddMinutes(1) };
        await stores.Codes.CreateAsync(code);

        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var copy = await stores.Codes.GetByCodeAsync("c1");
            return await stores.Codes.TryRedeemAsync(copy!);
        }));
        var results = await Task.WhenAll(attempts);

        results.Count(r => r).Should().Be(1);
        (await stores.Codes.GetByCodeAsync("c1"))!.Used.Should().BeTrue();
        (await stores.Codes.TryRedeemAsync(code)).Should().BeFalse("a used code cannot be redeemed again");
    }

    [Fact]
    public async Task Of_concurrent_revocations_of_one_refresh_token_exactly_one_succeeds()
    {
        var stores = NewStores();
        await stores.Refreshes.CreateAsync(new RefreshTokenRecord { TokenHash = "H", ClientId = "web", UserId = "u", ExpiresAt = Clock.UtcNow.AddDays(1) });

        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var copy = await stores.Refreshes.GetByHashAsync("H");
            return await stores.Refreshes.TryRevokeAsync(copy!);
        }));
        var results = await Task.WhenAll(attempts);

        results.Count(r => r).Should().Be(1);
        (await stores.Refreshes.GetByHashAsync("H"))!.Revoked.Should().BeTrue();
    }

    // ---- the server over these stores ----------------------------------------------------------------------------

    [Fact]
    public async Task Authorization_code_with_PKCE_issues_once()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("web", OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken));
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        await stores.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "code-1", ClientId = "web", UserId = "u", RedirectUri = "https://app.test/callback", Scope = "read",
            CodeChallenge = challenge, CodeChallengeMethod = "S256", ExpiresAt = Clock.UtcNow.AddMinutes(1),
        });
        var request = new TokenRequest
        {
            GrantType = OAuthGrantTypes.AuthorizationCode, ClientId = "web", Code = "code-1",
            RedirectUri = "https://app.test/callback", CodeVerifier = verifier,
        };

        var response = await server.Token.HandleAsync(request);
        response.RefreshToken.Should().NotBeNullOrEmpty();

        (await FluentActions.Awaiting(() => server.Token.HandleAsync(request)).Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }

    [Fact]
    public async Task Concurrent_redemptions_of_one_code_through_the_server_issue_one_pair()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("web", OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken));
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        await stores.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "code-1", ClientId = "web", UserId = "u", RedirectUri = "https://app.test/callback", Scope = "read",
            CodeChallenge = challenge, CodeChallengeMethod = "S256", ExpiresAt = Clock.UtcNow.AddMinutes(1),
        });

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                await server.Token.HandleAsync(new TokenRequest
                {
                    GrantType = OAuthGrantTypes.AuthorizationCode, ClientId = "web", Code = "code-1",
                    RedirectUri = "https://app.test/callback", CodeVerifier = verifier,
                });
                return true;
            }
            catch (OAuthServerException e) when (e.ErrorCode == OAuthErrorCodes.InvalidGrant)
            {
                return false;
            }
        })));

        outcomes.Count(o => o).Should().Be(1, "a code is single-use (CR-L351)");
    }

    [Fact]
    public async Task Device_code_flow_issues_after_approval()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("tv", OAuthGrantTypes.DeviceCode, OAuthGrantTypes.RefreshToken));

        var started = await server.DeviceAuthorization.HandleAsync(new DeviceAuthorizationRequest { ClientId = "tv", Scope = "read" });
        var poll = new TokenRequest { GrantType = OAuthGrantTypes.DeviceCode, ClientId = "tv", DeviceCode = started.DeviceCode };

        (await FluentActions.Awaiting(() => server.Token.HandleAsync(poll)).Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.AuthorizationPending);

        await server.DeviceAuthorization.ApproveAsync(started.UserCode, "user-7", approved: true);
        Clock.Advance(TimeSpan.FromSeconds(6));

        var issued = await server.Token.HandleAsync(poll);
        issued.RefreshToken.Should().NotBeNullOrEmpty();
        PayloadClaims(issued.AccessToken)["sub"].Should().Be("user-7");
    }

    [Fact]
    public async Task Federated_claims_survive_rotation_and_reuse_revokes_the_family()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("web", OAuthGrantTypes.RefreshToken));
        var claims = new Dictionary<string, string> { ["name"] = "octocat", ["roles"] = "admin" };

        var issued = await server.Token.IssueForSubjectAsync("web", "github:42", "read", claims);
        var refresh = new TokenRequest { GrantType = OAuthGrantTypes.RefreshToken, ClientId = "web", RefreshToken = issued.RefreshToken };
        var rotated = await server.Token.HandleAsync(refresh);

        var payload = PayloadClaims(rotated.AccessToken);
        payload["sub"].Should().Be("github:42");
        payload["name"].Should().Be("octocat", "the claims were persisted with the refresh record");
        payload["roles"].Should().Be("admin");

        (await FluentActions.Awaiting(() => server.Token.HandleAsync(refresh)).Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
        await FluentActions.Awaiting(() => server.Token.HandleAsync(new TokenRequest
            {
                GrantType = OAuthGrantTypes.RefreshToken, ClientId = "web", RefreshToken = rotated.RefreshToken,
            }))
            .Should().ThrowAsync<OAuthServerException>("reusing a rotated token revokes the family");
    }

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_issue_one_pair()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("web", OAuthGrantTypes.RefreshToken));
        var issued = await server.Token.IssueForSubjectAsync("web", "u", "read");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                await server.Token.HandleAsync(new TokenRequest
                {
                    GrantType = OAuthGrantTypes.RefreshToken, ClientId = "web", RefreshToken = issued.RefreshToken,
                });
                return true;
            }
            catch (OAuthServerException e) when (e.ErrorCode == OAuthErrorCodes.InvalidGrant)
            {
                return false;
            }
        })));

        outcomes.Count(o => o).Should().Be(1, "rotation claims the presented token once");
    }

    [Fact]
    public async Task An_expired_code_or_refresh_token_is_refused()
    {
        var stores = NewStores();
        var server = NewServer(stores);
        await stores.Clients.CreateAsync(NewPublicClient("web", OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken));
        await stores.Codes.CreateAsync(new AuthorizationCode
        {
            Code = "code-1", ClientId = "web", UserId = "u", RedirectUri = "https://app.test/callback", Scope = "read",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", CodeChallengeMethod = "S256",
            ExpiresAt = Clock.UtcNow.AddMinutes(1),
        });
        var issued = await server.Token.IssueForSubjectAsync("web", "u", "read");

        Clock.Advance(TimeSpan.FromDays(2));

        (await FluentActions.Awaiting(() => server.Token.HandleAsync(new TokenRequest
            {
                GrantType = OAuthGrantTypes.AuthorizationCode, ClientId = "web", Code = "code-1",
                RedirectUri = "https://app.test/callback", CodeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            })).Should().ThrowAsync<OAuthServerException>()).Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
        (await FluentActions.Awaiting(() => server.Token.HandleAsync(new TokenRequest
            {
                GrantType = OAuthGrantTypes.RefreshToken, ClientId = "web", RefreshToken = issued.RefreshToken,
            })).Should().ThrowAsync<OAuthServerException>()).Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidGrant);
    }
}
