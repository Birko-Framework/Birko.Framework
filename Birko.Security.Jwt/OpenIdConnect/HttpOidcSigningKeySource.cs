using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// Fetches provider signing keys from the published JWKS endpoint and caches them per provider.
/// <para>
/// Takes a plain <see cref="HttpClient"/> rather than <c>IHttpClientFactory</c> on purpose: this keeps
/// Birko.Security.Jwt free of a <c>Microsoft.Extensions.Http</c> dependency it does not otherwise need,
/// so a console or worker consumer can use it too. A DI-based consumer hands in a factory-created client.
/// </para>
/// <para>
/// A plain in-process cache is deliberate: these are public keys, the set is tiny, and a login must not
/// fail because a distributed cache is down.
/// </para>
/// </summary>
public class HttpOidcSigningKeySource : IOidcSigningKeySource
{
    /// <summary>Default reuse window for a fetched key set.</summary>
    public static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheDuration;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(IReadOnlyCollection<SecurityKey> Keys, DateTimeOffset FetchedAt);

    /// <param name="httpClient">Client used to GET the JWKS document.</param>
    /// <param name="cacheDuration">Key-set reuse window; defaults to <see cref="DefaultCacheDuration"/>.</param>
    public HttpOidcSigningKeySource(HttpClient httpClient, TimeSpan? cacheDuration = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDuration = cacheDuration ?? DefaultCacheDuration;
    }

    /// <summary>
    /// Last error encountered while fetching, or null. Exposed so a consumer can log the cause of an
    /// empty key set without this type taking a logging dependency.
    /// </summary>
    public Exception? LastFetchError { get; private set; }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default)
    {
        if (!forceRefresh
            && _cache.TryGetValue(provider, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAt < _cacheDuration)
        {
            return cached.Keys;
        }

        try
        {
            var json = await _httpClient.GetStringAsync(options.JwksUri, ct).ConfigureAwait(false);
            IReadOnlyCollection<SecurityKey> keys = JsonWebKeySet.Create(json).GetSigningKeys().ToArray();

            if (keys.Count == 0)
            {
                // Published, but nothing usable. Do not cache — a corrected document should take effect
                // immediately rather than after the TTL.
                LastFetchError = new SecurityTokenException(
                    $"JWKS at '{options.JwksUri}' contained no usable signing keys.");
                return keys;
            }

            LastFetchError = null;
            _cache[provider] = new CacheEntry(keys, DateTimeOffset.UtcNow);
            return keys;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastFetchError = ex;

            // Serving a stale key set beats refusing every login over a transient network blip — the keys
            // are still provider-published, so nothing is accepted that the provider did not sign.
            if (_cache.TryGetValue(provider, out var stale))
                return stale.Keys;

            return Array.Empty<SecurityKey>();
        }
    }
}
