using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth;

/// <summary>
/// OAuth2 client interface for obtaining and managing access tokens.
/// </summary>
public interface IOAuthClient : IDisposable
{
    /// <summary>
    /// Gets a valid access token, automatically refreshing if expired.
    /// Returns a cached token when still valid.
    /// </summary>
    Task<OAuthToken> GetTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces a new token request, ignoring any cached token.
    /// </summary>
    Task<OAuthToken> RefreshTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Exchanges an authorization code for tokens (Authorization Code / PKCE flows).
    /// </summary>
    /// <param name="code">The authorization code received from the authorization server.</param>
    /// <param name="codeVerifier">The PKCE code verifier (required for PKCE flow, null for standard).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OAuthToken> ExchangeCodeAsync(string code, string? codeVerifier = null, CancellationToken ct = default);

    /// <summary>
    /// Starts the Device Code flow by requesting device and user codes.
    /// </summary>
    Task<DeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(CancellationToken ct = default);

    /// <summary>
    /// Polls the token endpoint until the user authorizes the device or the request expires.
    /// </summary>
    /// <param name="deviceCode">The device code from <see cref="RequestDeviceAuthorizationAsync"/>.</param>
    /// <param name="intervalSeconds">Polling interval override (null = use server-provided or settings default).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OAuthToken> PollDeviceTokenAsync(string deviceCode, int? intervalSeconds = null, CancellationToken ct = default);

    /// <summary>
    /// Builds the authorization URL for Authorization Code / PKCE flows.
    /// The caller should redirect the user to this URL.
    /// </summary>
    /// <param name="state">Anti-CSRF state parameter.</param>
    /// <param name="pkceChallenge">Optional PKCE challenge (required for PKCE flow).</param>
    string BuildAuthorizationUrl(string state, PkceChallenge? pkceChallenge = null);

    /// <summary>
    /// Clears the cached token.
    /// </summary>
    void ClearTokenCache();
}
