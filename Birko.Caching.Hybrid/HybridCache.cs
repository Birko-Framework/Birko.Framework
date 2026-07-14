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
    private readonly Dictionary<string, KeyLock> _locks = new();
    private readonly object _locksGate = new();
    private bool _disposed;

    /// <summary>
    /// A per-key stampede lock with a reference count so it can be removed once no caller holds
    /// or awaits it — otherwise <see cref="_locks"/> would grow unbounded, one entry per distinct
    /// key, for the process lifetime (CR-H013).
    /// </summary>
    private sealed class KeyLock
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

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

        // Populate L1 from an L2 hit. This intentionally uses GetL1Options(null) → the L1DefaultExpiration
        // cap (not the entry's remaining L2 lifetime, which this read path doesn't know), to bound how
        // stale an L1 copy can be. Note this differs from GetOrSetAsync, which threads the caller's options
        // through GetL1Options(options); the two read paths can therefore give the same key different L1
        // TTLs (CR-L038, documented as intended).
        var l1Options = GetL1Options(null);
        await _l1.SetAsync(key, l2Result.Value, l1Options, ct);

        return l2Result;
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        var l1Options = GetL1Options(options);

        if (_options.WriteThrough)
        {
            // Write both tiers in parallel, but always await the L1 write so it is never orphaned as
            // unobserved fire-and-forget when L2 faults with fallback disabled (CR-M031). The L2 call
            // is inside the try so a synchronous throw (not just a faulted task) is handled too.
            var l1Task = _l1.SetAsync(key, value, l1Options, ct);

            try
            {
                await _l2.SetAsync(key, value, options, ct);
            }
            catch when (_options.FallbackToL1OnL2Failure)
            {
                // Fallback enabled: tolerate the L2 failure (L1 still observed in finally).
            }
            finally
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

        // Per-key lock to prevent cache stampede. Reference-counted so the entry is removed once
        // the last caller releases it (CR-H013: the map used to grow one lock per key forever).
        KeyLock keyLock;
        lock (_locksGate)
        {
            if (!_locks.TryGetValue(key, out keyLock!))
            {
                keyLock = new KeyLock();
                _locks[key] = keyLock;
            }
            keyLock.RefCount++;
        }

        var acquired = false;
        try
        {
            await keyLock.Semaphore.WaitAsync(ct);
            acquired = true;

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
            if (acquired)
                keyLock.Semaphore.Release();

            lock (_locksGate)
            {
                if (--keyLock.RefCount == 0)
                {
                    _locks.Remove(key);
                    keyLock.Semaphore.Dispose();
                }
            }
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
    // internal (was private) so the CR-L039 matrix test can pin the exact CacheEntryOptions produced.
    internal CacheEntryOptions GetL1Options(CacheEntryOptions? requested)
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

        lock (_locksGate)
        {
            foreach (var kvp in _locks)
                kvp.Value.Semaphore.Dispose();
            _locks.Clear();
        }

        // Hybrid cache does NOT dispose L1/L2 — caller owns their lifetime
    }
}
