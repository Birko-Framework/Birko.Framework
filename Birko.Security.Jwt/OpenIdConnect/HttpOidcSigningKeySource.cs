using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Time;
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

    /// <summary>Default age past which a cached key set is no longer served while the JWKS endpoint fails.</summary>
    public static readonly TimeSpan DefaultMaxStaleAge = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _maxStaleAge;
    private readonly IDateTimeProvider _clock;
    // SH-L239: keyed by provider AND JWKS URI, so a provider reconfigured to another endpoint is fetched afresh rather than
    // served the old endpoint's keys for the rest of the cache window.
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // SH-L239: one fetch per key at a time; a burst of logins at expiry waits for it instead of each issuing a GET.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    private static string CacheKey(string provider, OidcProviderOptions options) => provider + "|" + options.JwksUri;

    private sealed record CacheEntry(IReadOnlyCollection<SecurityKey> Keys, DateTimeOffset FetchedAt);

    /// <param name="httpClient">Client used to GET the JWKS document.</param>
    /// <param name="cacheDuration">Key-set reuse window; defaults to <see cref="DefaultCacheDuration"/>.</param>
    /// <param name="maxStaleAge">
    /// How old a cached key set may be and still be served when a fetch fails; defaults to
    /// <see cref="DefaultMaxStaleAge"/>. Past it, a failed fetch answers no keys, so logins fail until the endpoint is back.
    /// </param>
    /// <param name="clock">Clock for the cache ages; the system clock by default.</param>
    public HttpOidcSigningKeySource(HttpClient httpClient, TimeSpan? cacheDuration = null, TimeSpan? maxStaleAge = null, IDateTimeProvider? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDuration = cacheDuration ?? DefaultCacheDuration;
        _maxStaleAge = maxStaleAge ?? DefaultMaxStaleAge;
        _clock = clock ?? new SystemDateTimeProvider();
    }

    /// <summary>
    /// True when the last answer was a cached key set served because the fetch failed. Exposed, like
    /// <see cref="LastFetchError"/>, so a consumer can report that it is verifying against old keys.
    /// </summary>
    public bool LastServedStale { get; private set; }

    /// <summary>
    /// Last error encountered while fetching, or null. Exposed so a consumer can log the cause of an
    /// empty key set without this type taking a logging dependency.
    /// </summary>
    public Exception? LastFetchError { get; private set; }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        string provider, OidcProviderOptions options, bool forceRefresh, CancellationToken ct = default)
    {
        var key = CacheKey(provider, options);
        var asked = _clock.OffsetUtcNow;
        if (!forceRefresh
            && _cache.TryGetValue(key, out var cached)
            && asked - cached.FetchedAt < _cacheDuration)
        {
            LastServedStale = false;
            return cached.Keys;
        }

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have fetched while this one waited. A forced refresh takes only a fetch made after it
            // was asked for; an ordinary call takes any fresh one.
            if (_cache.TryGetValue(key, out var fetched)
                && (forceRefresh ? fetched.FetchedAt >= asked : _clock.OffsetUtcNow - fetched.FetchedAt < _cacheDuration))
            {
                LastServedStale = false;
                return fetched.Keys;
            }
            return await FetchAsync(key, options, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyCollection<SecurityKey>> FetchAsync(string key, OidcProviderOptions options, CancellationToken ct)
    {
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
            LastServedStale = false;
            _cache[key] = new CacheEntry(keys, _clock.OffsetUtcNow);
            return keys;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastFetchError = ex;

            // Serving a stale key set beats refusing every login over a transient network blip — the keys
            // are still provider-published, so nothing is accepted that the provider did not sign. SH-M259: but
            // not for ever. A key the provider revoked (a compromised one) stayed accepted for as long as the
            // endpoint was down, so past the maximum stale age a failed fetch answers no keys.
            if (_cache.TryGetValue(key, out var stale) && _clock.OffsetUtcNow - stale.FetchedAt < _maxStaleAge)
            {
                LastServedStale = true;
                return stale.Keys;
            }

            LastServedStale = false;
            return Array.Empty<SecurityKey>();
        }
    }
}
