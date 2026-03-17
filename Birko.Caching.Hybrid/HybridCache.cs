using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Caching.Hybrid;

/// <summary>
/// Two-tier hybrid cache: L1 (local memory) for speed, L2 (distributed) for consistency across nodes.
/// Reads check L1 first, fall back to L2, and populate L1 on L2 hits.
/// Writes go to both tiers (write-through).
/// </summary>
public sealed class HybridCache : ICache
{
    private readonly ICache _l1;
    private readonly ICache _l2;
    private readonly HybridCacheOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private bool _disposed;

    /// <summary>
    /// Creates a hybrid cache with the given L1 (memory) and L2 (distributed) caches.
    /// </summary>
    /// <param name="l1">Local in-memory cache (fast, per-process).</param>
    /// <param name="l2">Distributed cache (Redis, etc.) shared across nodes.</param>
    /// <param name="options">Hybrid cache configuration. Uses defaults if null.</param>
    public HybridCache(ICache l1, ICache l2, HybridCacheOptions? options = null)
    {
        _l1 = l1 ?? throw new ArgumentNullException(nameof(l1));
        _l2 = l2 ?? throw new ArgumentNullException(nameof(l2));
        _options = options ?? new HybridCacheOptions();
    }

    public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        // Check L1 first
        var l1Result = await _l1.GetAsync<T>(key, ct);
        if (l1Result.HasValue)
            return l1Result;

        // Fall back to L2
        CacheResult<T> l2Result;
        try
        {
            l2Result = await _l2.GetAsync<T>(key, ct);
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            return CacheResult<T>.Miss();
        }

        if (!l2Result.HasValue)
            return CacheResult<T>.Miss();

        // Populate L1 from L2 hit
        var l1Options = GetL1Options(null);
        await _l1.SetAsync(key, l2Result.Value, l1Options, ct);

        return l2Result;
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        var l1Options = GetL1Options(options);

        if (_options.WriteThrough)
        {
            // Write both tiers in parallel
            var l1Task = _l1.SetAsync(key, value, l1Options, ct);
            Task l2Task;

            try
            {
                l2Task = _l2.SetAsync(key, value, options, ct);
                await Task.WhenAll(l1Task, l2Task);
            }
            catch when (_options.FallbackToL1OnL2Failure)
            {
                await l1Task;
            }
        }
        else
        {
            // Write L2 first for durability, then L1
            try
            {
                await _l2.SetAsync(key, value, options, ct);
            }
            catch when (!_options.FallbackToL1OnL2Failure)
            {
                throw;
            }
            catch
            {
                // L2 failed, still populate L1
            }

            await _l1.SetAsync(key, value, l1Options, ct);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        // Remove from L1 immediately, then L2
        var l1Task = _l1.RemoveAsync(key, ct);

        try
        {
            var l2Task = _l2.RemoveAsync(key, ct);
            await Task.WhenAll(l1Task, l2Task);
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            await l1Task;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (await _l1.ExistsAsync(key, ct))
            return true;

        try
        {
            return await _l2.ExistsAsync(key, ct);
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            return false;
        }
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        // Check L1
        var l1Result = await _l1.GetAsync<T>(key, ct);
        if (l1Result.HasValue)
            return l1Result.Value!;

        // Check L2
        try
        {
            var l2Result = await _l2.GetAsync<T>(key, ct);
            if (l2Result.HasValue)
            {
                await _l1.SetAsync(key, l2Result.Value, GetL1Options(options), ct);
                return l2Result.Value!;
            }
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            // L2 unavailable, proceed to factory
        }

        // Per-key lock to prevent cache stampede
        var keyLock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
        try
        {
            // Double-check L1 after acquiring lock
            l1Result = await _l1.GetAsync<T>(key, ct);
            if (l1Result.HasValue)
                return l1Result.Value!;

            var value = await factory(ct);
            await SetAsync(key, value, options, ct);
            return value;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var l1Task = _l1.RemoveByPrefixAsync(prefix, ct);

        try
        {
            var l2Task = _l2.RemoveByPrefixAsync(prefix, ct);
            await Task.WhenAll(l1Task, l2Task);
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            await l1Task;
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var l1Task = _l1.ClearAsync(ct);

        try
        {
            var l2Task = _l2.ClearAsync(ct);
            await Task.WhenAll(l1Task, l2Task);
        }
        catch when (_options.FallbackToL1OnL2Failure)
        {
            await l1Task;
        }
    }

    /// <summary>
    /// Builds L1 cache entry options capped by <see cref="HybridCacheOptions.L1MaxExpiration"/>.
    /// </summary>
    private CacheEntryOptions GetL1Options(CacheEntryOptions? requested)
    {
        var maxExpiry = _options.L1MaxExpiration;

        if (requested == null)
        {
            return CacheEntryOptions.Absolute(_options.L1DefaultExpiration);
        }

        if (maxExpiry == null)
        {
            // No cap — use original options for L1
            return requested;
        }

        // Cap absolute expiration to L1MaxExpiration
        var absolute = requested.AbsoluteExpiration.HasValue
            ? (requested.AbsoluteExpiration.Value < maxExpiry.Value ? requested.AbsoluteExpiration.Value : maxExpiry.Value)
            : (maxExpiry.Value < _options.L1DefaultExpiration ? maxExpiry.Value : _options.L1DefaultExpiration);

        var sliding = requested.SlidingExpiration.HasValue
            ? (requested.SlidingExpiration.Value < maxExpiry.Value ? requested.SlidingExpiration.Value : maxExpiry.Value)
            : (TimeSpan?)null;

        return new CacheEntryOptions
        {
            AbsoluteExpiration = absolute,
            SlidingExpiration = sliding,
            Priority = requested.Priority
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _locks)
            kvp.Value.Dispose();
        _locks.Clear();

        // Hybrid cache does NOT dispose L1/L2 — caller owns their lifetime
    }
}
