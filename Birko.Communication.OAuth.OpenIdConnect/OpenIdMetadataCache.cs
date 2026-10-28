using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>The provider could not be reached or answered something unusable (discovery, keys or the code exchange).</summary>
public sealed class OpenIdProviderUnavailableException : OAuthException
{
    public OpenIdProviderUnavailableException(string message) : base(message) { }
    public OpenIdProviderUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>What the discovery document and its key set say, as fetched at <see cref="FetchedAt"/>.</summary>
internal sealed record OpenIdMetadata(
    string Issuer, string AuthorizationEndpoint, string TokenEndpoint, IReadOnlyList<SecurityKey> SigningKeys, DateTimeOffset FetchedAt);

/// <summary>
/// The discovery document and signing keys of one provider, fetched on first use and again after
/// <see cref="OpenIdProviderOptions.MetadataRefreshInterval"/>, on the injected clock (TASK-778). Not
/// Microsoft.IdentityModel's <c>ConfigurationManager</c>, which may refresh in the background and hand back the stale
/// set meanwhile: here a key miss refreshes at once, rate-limited by
/// <see cref="OpenIdProviderOptions.MinimumMetadataRefreshInterval"/>, and every step follows the clock tests set.
/// </summary>
internal sealed class OpenIdMetadataCache
{
    private readonly OpenIdProviderOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OpenIdMetadata? _current;
    private DateTimeOffset? _lastAttempt;

    public OpenIdMetadataCache(OpenIdProviderOptions options, HttpClient http, TimeProvider clock)
    {
        _options = options;
        _http = http;
        _clock = clock;
    }

    /// <summary>The cached metadata, fetched first when there is none or it is older than the refresh interval. A failed
    /// refresh keeps serving the cached set; with nothing cached it throws.</summary>
    /// <exception cref="OpenIdProviderUnavailableException">Nothing is cached and the fetch failed.</exception>
    public async Task<OpenIdMetadata> GetAsync(CancellationToken ct)
    {
        var current = _current;
        if (current != null && _clock.GetUtcNow() - current.FetchedAt < _options.MetadataRefreshInterval) return current;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            current = _current;
            if (current != null && _clock.GetUtcNow() - current.FetchedAt < _options.MetadataRefreshInterval) return current;
            // A provider that is down is not asked again on every request while the stale set still works.
            if (current != null && _lastAttempt is { } last && _clock.GetUtcNow() - last < _options.MinimumMetadataRefreshInterval)
                return current;
            return await FetchLockedAsync(current, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// A token named a key the cached set does not hold, or failed its signature. Fetches again, unless the last fetch
    /// was within the minimum refresh interval; returns the new metadata, or null when nothing was fetched or it failed.
    /// </summary>
    public async Task<OpenIdMetadata?> RefreshAfterKeyMissAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_lastAttempt is { } last && _clock.GetUtcNow() - last < _options.MinimumMetadataRefreshInterval) return null;
            var before = _current;
            try
            {
                var fetched = await FetchLockedAsync(before, ct).ConfigureAwait(false);
                return ReferenceEquals(fetched, before) ? null : fetched;
            }
            catch (OpenIdProviderUnavailableException)
            {
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<OpenIdMetadata> FetchLockedAsync(OpenIdMetadata? fallback, CancellationToken ct)
    {
        _lastAttempt = _clock.GetUtcNow();
        try
        {
            using var discovery = JsonDocument.Parse(await GetStringAsync(_options.MetadataAddress, ct).ConfigureAwait(false));
            var root = discovery.RootElement;
            var issuer = RequiredString(root, "issuer");
            var authorization = RequiredHttpsUrl(root, "authorization_endpoint");
            var token = RequiredHttpsUrl(root, "token_endpoint");
            var jwksUri = RequiredHttpsUrl(root, "jwks_uri");

            var keySet = new JsonWebKeySet(await GetStringAsync(jwksUri, ct).ConfigureAwait(false));
            var keys = keySet.GetSigningKeys();
            if (keys.Count == 0) throw new OpenIdProviderUnavailableException("The provider's key set holds no signing key.");

            _current = new OpenIdMetadata(issuer, authorization, token, new List<SecurityKey>(keys), _clock.GetUtcNow());
            return _current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (fallback != null) return fallback;
            throw ex as OpenIdProviderUnavailableException
                ?? new OpenIdProviderUnavailableException($"The provider's metadata could not be read: {ex.Message}", ex);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new OpenIdProviderUnavailableException($"{url} answered {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())
            ? value.GetString()!
            : throw new OpenIdProviderUnavailableException($"The discovery document has no {name}.");

    private static string RequiredHttpsUrl(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? value
            : throw new OpenIdProviderUnavailableException($"The discovery document's {name} is not an https URL.");
    }
}
