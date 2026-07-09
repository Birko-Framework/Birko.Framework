using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching.Serialization;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.Caching.Redis;

/// <summary>
/// Redis-backed ICache implementation using StackExchange.Redis.
/// The consuming project must reference the StackExchange.Redis NuGet package.
/// </summary>
public sealed class RedisCache : ICache
{
    private readonly RedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly TimeSpan _defaultExpiration;
    private readonly bool _ownsConnection;
    private bool _disposed;

    /// <summary>
    /// Creates a new RedisCache that owns its connection.
    /// </summary>
    /// <param name="settings">Redis connection settings.</param>
    /// <param name="defaultExpiration">Default expiration for entries without explicit options. Defaults to 5 minutes.</param>
    public RedisCache(RedisSettings settings, TimeSpan? defaultExpiration = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionManager = new RedisConnectionManager(settings);
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(5);
        _ownsConnection = true;
    }

    /// <summary>
    /// Creates a new RedisCache using a shared connection manager.
    /// </summary>
    /// <param name="connectionManager">A pre-configured connection manager.</param>
    /// <param name="settings">Redis settings (for key prefix configuration).</param>
    /// <param name="defaultExpiration">Default expiration for entries without explicit options. Defaults to 5 minutes.</param>
    public RedisCache(RedisConnectionManager connectionManager, RedisSettings settings, TimeSpan? defaultExpiration = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(5);
        _ownsConnection = false;
    }

    public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        var value = await db.StringGetAsync(fullKey);

        if (!value.HasValue)
            return CacheResult<T>.Miss();

        // Refresh sliding expiration on hit
        await RefreshSlidingExpirationAsync(db, fullKey);

        return CacheResult<T>.Hit(CacheSerializer.Deserialize<T>((byte[])value!)!);
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        var opts = options ?? new CacheEntryOptions { AbsoluteExpiration = _defaultExpiration };

        var serialized = CacheSerializer.Serialize(value);
        var expiry = GetExpiry(opts);

        await db.StringSetAsync(fullKey, serialized, expiry);

        // Store sliding expiration metadata if needed. The absolute cap is persisted as a fixed
        // DEADLINE (unix seconds), not the original window — otherwise every refresh recomputes
        // min(sliding, window) against a constant window and the entry lives forever (CR-H014).
        if (opts.SlidingExpiration.HasValue)
        {
            var absoluteDeadline = opts.AbsoluteExpiration.HasValue
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)opts.AbsoluteExpiration.Value.TotalSeconds
                : -1;
            await db.HashSetAsync(GetMetaKey(fullKey), [
                new HashEntry("sliding", opts.SlidingExpiration.Value.TotalSeconds),
                new HashEntry("absoluteDeadline", absoluteDeadline)
            ]);
            await db.KeyExpireAsync(GetMetaKey(fullKey), expiry);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        await db.KeyDeleteAsync([fullKey, GetMetaKey(fullKey)]);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        return await db.KeyExistsAsync(GetFullKey(key));
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await GetAsync<T>(key, ct);
        if (result.HasValue)
            return result.Value!;

        // Use Redis SET NX as distributed lock
        var db = _connectionManager.GetDatabase();
        var lockKey = GetFullKey($"__lock:{key}");
        var lockAcquired = await db.StringSetAsync(lockKey, "1", TimeSpan.FromSeconds(30), When.NotExists);

        try
        {
            if (lockAcquired)
            {
                // We got the lock — create the value
                var value = await factory(ct);
                await SetAsync(key, value, options, ct);
                return value;
            }
            else
            {
                // Another caller is creating — wait and retry
                await Task.Delay(50, ct);
                result = await GetAsync<T>(key, ct);
                if (result.HasValue)
                    return result.Value!;

                // Fallback: create anyway (lock holder may have failed)
                var value = await factory(ct);
                await SetAsync(key, value, options, ct);
                return value;
            }
        }
        finally
        {
            if (lockAcquired)
                await db.KeyDeleteAsync(lockKey);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var server = _connectionManager.GetServer();
        var fullPrefix = GetFullKey(prefix);

        await foreach (var key in server.KeysAsync(pattern: $"{fullPrefix}*", database: _settings.Database))
        {
            ct.ThrowIfCancellationRequested();
            await db.KeyDeleteAsync(key);
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_settings.KeyPrefix is not null)
        {
            await RemoveByPrefixAsync("", ct);
        }
        else
        {
            var server = _connectionManager.GetServer();
            await server.FlushDatabaseAsync(_settings.Database);
        }
    }

    private string GetFullKey(string key) =>
        _settings.KeyPrefix is not null ? $"{_settings.KeyPrefix}:{key}" : key;

    private static RedisKey GetMetaKey(string fullKey) => $"{fullKey}:__meta";

    private static TimeSpan? GetExpiry(CacheEntryOptions opts)
    {
        if (opts.AbsoluteExpiration.HasValue)
            return opts.AbsoluteExpiration;
        if (opts.SlidingExpiration.HasValue)
            return opts.SlidingExpiration;
        return null;
    }

    private async Task RefreshSlidingExpirationAsync(IDatabase db, string fullKey)
    {
        var metaKey = GetMetaKey(fullKey);
        var sliding = await db.HashGetAsync(metaKey, "sliding");
        if (!sliding.HasValue) return;

        var slidingSeconds = (double)sliding;
        if (slidingSeconds <= 0) return;

        var absolute = await db.HashGetAsync(metaKey, "absoluteDeadline");
        var absoluteDeadline = absolute.HasValue ? (long)absolute : -1;

        var newExpiry = ComputeRefreshedTtl(slidingSeconds, absoluteDeadline, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (newExpiry == null)
        {
            // Past the absolute deadline — expire now instead of re-extending.
            await db.KeyDeleteAsync([fullKey, metaKey]);
            return;
        }

        await db.KeyExpireAsync(fullKey, newExpiry.Value);
        await db.KeyExpireAsync(metaKey, newExpiry.Value);
    }

    /// <summary>
    /// Computes the refreshed TTL for a sliding entry, capped by the absolute deadline (CR-H014).
    /// Returns min(sliding, time-remaining-to-deadline); or the full sliding span when there is no
    /// absolute cap; or <c>null</c> when the deadline has already passed (caller should expire the
    /// entry). Because the cap is a fixed deadline, the remaining budget strictly shrinks over
    /// time, so an always-accessed entry can no longer live forever.
    /// </summary>
    internal static TimeSpan? ComputeRefreshedTtl(double slidingSeconds, long absoluteDeadlineUnix, long nowUnix)
    {
        var slidingSpan = TimeSpan.FromSeconds(slidingSeconds);
        if (absoluteDeadlineUnix <= 0)
            return slidingSpan; // no absolute cap

        var remaining = absoluteDeadlineUnix - nowUnix;
        if (remaining <= 0)
            return null; // past the deadline

        var remainingSpan = TimeSpan.FromSeconds(remaining);
        return slidingSpan < remainingSpan ? slidingSpan : remainingSpan;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsConnection)
        {
            _connectionManager.Dispose();
        }
    }
}
