using System;

namespace Birko.Communication.OAuth;

/// <summary>
/// Represents an OAuth2 token response.
/// </summary>
public class OAuthToken
{
    /// <summary>
    /// The access token string.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// The token type (typically "Bearer").
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// UTC time when the access token expires.
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Optional refresh token for obtaining new access tokens.
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Space-separated scopes granted by the authorization server.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Optional ID token (OpenID Connect).
    /// </summary>
    public string? IdToken { get; init; }

    /// <summary>
    /// Whether the access token has expired (considering no buffer).
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Whether the access token has expired considering the specified buffer.
    /// </summary>
    public bool IsExpired(int bufferSeconds) => DateTime.UtcNow >= ExpiresAt.AddSeconds(-bufferSeconds);
}

/// <summary>
/// Represents the device authorization response for the Device Code flow.
/// </summary>
public class DeviceAuthorizationResponse
{
    /// <summary>
    /// The device verification code.
    /// </summary>
    public string DeviceCode { get; init; } = string.Empty;

    /// <summary>
    /// The end-user verification code to display.
    /// </summary>
    public string UserCode { get; init; } = string.Empty;

    /// <summary>
    /// The verification URI the user should visit.
    /// </summary>
    public string VerificationUri { get; init; } = string.Empty;

    /// <summary>
    /// Optional verification URI with the user code pre-filled.
    /// </summary>
    public string? VerificationUriComplete { get; init; }

    /// <summary>
    /// Lifetime in seconds of the device code.
    /// </summary>
    public int ExpiresInSeconds { get; init; }

    /// <summary>
    /// Minimum polling interval in seconds.
    /// </summary>
    public int IntervalSeconds { get; init; } = 5;
}
