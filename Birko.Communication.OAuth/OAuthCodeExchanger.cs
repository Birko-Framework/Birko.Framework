using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth;

/// <summary>
/// Exchanges an authorization-code callback's <c>code</c> for the provider's token. The seam an app's sign-in callback
/// depends on, so its tests can fake the provider instead of calling it.
/// </summary>
public interface IOAuthCodeExchanger
{
    /// <summary>Exchanges <paramref name="code"/> (with the PKCE verifier when the sign-in used one).</summary>
    /// <exception cref="OAuthException">The provider refused the code (an expired or reused code, a wrong redirect URI).</exception>
    Task<OAuthToken> ExchangeAsync(string code, string? codeVerifier = null, CancellationToken ct = default);
}

/// <summary>
/// <see cref="OAuthClient"/>-backed <see cref="IOAuthCodeExchanger"/>. Safe as a singleton or as a typed
/// <c>HttpClient</c> client: each exchange uses its own <see cref="OAuthClient"/>, so no user's token is cached between
/// sign-ins, and a shared <see cref="HttpClient"/> is never reconfigured.
/// </summary>
public sealed class OAuthCodeExchanger : IOAuthCodeExchanger
{
    private readonly OAuthSettings _settings;
    private readonly HttpClient _http;

    /// <param name="settings">Authorization-code settings (token endpoint, client id and secret, redirect URI).</param>
    /// <param name="http">The HttpClient requests go through; not disposed by this class.</param>
    public OAuthCodeExchanger(OAuthSettings settings, HttpClient http)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<OAuthToken> ExchangeAsync(string code, string? codeVerifier = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("A code is required.", nameof(code));
        using var client = new OAuthClient(_settings, _http);
        return await client.ExchangeCodeAsync(code, codeVerifier, ct).ConfigureAwait(false);
    }
}
