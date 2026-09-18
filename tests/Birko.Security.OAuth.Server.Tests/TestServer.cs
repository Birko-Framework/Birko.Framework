using Birko.Security;
using Birko.Security.Jwt;
using Birko.Security.OAuth.Server;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server.Tests;

/// <summary>
/// Fixture used by every handler test — wires an <see cref="OAuthServer"/> with in-memory
/// stores and a frozen <see cref="TestDateTimeProvider"/> so expiry windows are deterministic.
/// </summary>
public class TestServer
{
    public TestDateTimeProvider Clock { get; } = new(DateTime.UtcNow);
    public OAuthServerSettings Settings { get; }
    public TokenOptions TokenOptions { get; }
    public JwtTokenProvider TokenProvider { get; }
    public IOAuthClientStore Clients { get; } = new InMemoryClientStore();
    public IAuthorizationCodeStore Codes { get; } = new InMemoryAuthorizationCodeStore();
    public IRefreshTokenStore Refreshes { get; } = new InMemoryRefreshTokenStore();
    public IDeviceCodeStore Devices { get; } = new InMemoryDeviceCodeStore();
    public IConsentStore Consents { get; } = new InMemoryConsentStore();
    public OAuthServer Server { get; }

    public TestServer()
    {
        Settings = new OAuthServerSettings
        {
            Location = "https://auth.test/",
            Issuer = "https://auth.test/",
            AccessTokenLifetimeSeconds = 3600,
            RefreshTokenLifetimeSeconds = 60 * 60 * 24,
            AuthorizationCodeLifetimeSeconds = 60,
            DeviceCodeLifetimeSeconds = 600,
            DeviceCodePollingIntervalSeconds = 5,
            RotateRefreshTokens = true,
            RequirePkceForPublicClients = true
        };
        TokenOptions = new TokenOptions
        {
            Secret = "test-secret-test-secret-test-secret-test-secret",
            Issuer = "https://auth.test/",
            Audience = "https://api.test/",
            ExpirationMinutes = 60
        };
        TokenProvider = new JwtTokenProvider(TokenOptions, Clock);
        Server = new OAuthServer(Settings, TokenProvider, TokenOptions,
            Clients, Codes, Refreshes, Devices, Consents,
            deviceVerificationUri: "https://auth.test/device",
            clock: Clock);
    }

    public OAuthClient RegisterConfidentialClient(string clientId, string secret, params string[] grantTypes)
    {
        var client = new OAuthClient
        {
            ClientId = clientId,
            ClientSecretHash = ClientSecretHasher.Hash(secret),
            ClientType = OAuthClientType.Confidential,
            Name = clientId,
            RedirectUris = { "https://app.test/callback" },
            AllowedGrantTypes = grantTypes.ToList(),
            AllowedScopes = { "read", "write" },
            CreatedAt = Clock.UtcNow,
            IsEnabled = true
        };
        Clients.CreateAsync(client).GetAwaiter().GetResult();
        return client;
    }

    public OAuthClient RegisterPublicClient(string clientId, params string[] grantTypes)
    {
        var client = new OAuthClient
        {
            ClientId = clientId,
            ClientSecretHash = null,
            ClientType = OAuthClientType.Public,
            Name = clientId,
            RedirectUris = { "https://app.test/callback" },
            AllowedGrantTypes = grantTypes.ToList(),
            AllowedScopes = { "read", "write" },
            CreatedAt = Clock.UtcNow,
            IsEnabled = true
        };
        Clients.CreateAsync(client).GetAwaiter().GetResult();
        return client;
    }
}
