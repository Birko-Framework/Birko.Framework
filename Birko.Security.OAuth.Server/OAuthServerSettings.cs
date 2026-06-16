using System.Collections.Generic;
using Birko.Configuration;

namespace Birko.Security.OAuth.Server;

/// <summary>
/// Configuration for the OAuth2 authorization server.
/// <para>
/// Location = the issuer URL ("iss" claim) published in tokens.
/// </para>
/// </summary>
public class OAuthServerSettings : Settings
{
    /// <summary>
    /// Lifetime of issued access tokens, in seconds. Default 3600 (1 hour).
    /// </summary>
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;

    /// <summary>
    /// Lifetime of issued refresh tokens, in seconds. Default 1209600 (14 days).
    /// </summary>
    public int RefreshTokenLifetimeSeconds { get; set; } = 60 * 60 * 24 * 14;

    /// <summary>
    /// Lifetime of authorization codes, in seconds. Per RFC 6749 §4.1.2 should be short. Default 60.
    /// </summary>
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Lifetime of device codes, in seconds. Default 600 (10 minutes).
    /// </summary>
    public int DeviceCodeLifetimeSeconds { get; set; } = 600;

    /// <summary>
    /// Minimum polling interval clients should use when polling the token endpoint
    /// during the Device Code flow. Default 5 seconds (RFC 8628 §3.5).
    /// </summary>
    public int DeviceCodePollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Issuer ("iss" claim) value embedded in issued tokens.
    /// If null, falls back to <see cref="Settings.Location"/>.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Whether refresh tokens are rotated (a new refresh token is issued on every refresh
    /// and the previous one is revoked). Default <c>true</c> (RFC 6819 §5.2.2.3).
    /// </summary>
    public bool RotateRefreshTokens { get; set; } = true;

    /// <summary>
    /// Whether public clients (no secret) must use PKCE. Default <c>true</c>.
    /// </summary>
    public bool RequirePkceForPublicClients { get; set; } = true;

    /// <summary>
    /// Scopes this server is willing to issue. If empty, any requested scope is allowed
    /// (the server will only narrow to what the client is registered for).
    /// </summary>
    public HashSet<string> SupportedScopes { get; set; } = new();

    /// <summary>
    /// Resolves the effective issuer value: <see cref="Issuer"/> if set, otherwise <see cref="Settings.Location"/>.
    /// </summary>
    public string GetIssuer() => string.IsNullOrWhiteSpace(Issuer) ? (Location ?? string.Empty) : Issuer!;
}
