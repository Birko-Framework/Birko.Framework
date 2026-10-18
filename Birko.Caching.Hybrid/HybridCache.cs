using System;
using System.Collections.Generic;
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
    private int _disposed;

    /// <summary>
    /// Invalidation generations, striped by key hash (SH-M048). A removal bumps the key's stripe between
    /// its L2 and its L1 removal; an L1 backfill from L2 captures the generation before reading L2 and
    /// withdraws its backfill if the generation moved. Striping keeps the memory fixed; a collision only
    /// costs a skipped backfill (the next read goes to L2), never a stale one.
    /// </summary>
    private readonly int[] _generations = new int[256];

    /// <summary>Bumped by prefix removal and clear, which may touch any stripe.</summary>
    private int _epoch;

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
        ThrowIfDisposed();
        // Check L1 first
        var l1Result = await _l1.GetAsync<T>(key, ct);
        if (l1Result.HasValue)
            return l1Result;

        // Fall back to L2
        var generation = CaptureGeneration(key);
        CacheResult<T> l2Result;
        try
        {
            l2Result = await _l2.GetAsync<T>(key, ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
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
        await BackfillL1Async(key, l2Result.Value, l1Options, generation, ct);

        return l2Result;
    }

    /// <remarks>
    /// The key's generation is bumped once L2 holds the new value, and L1 is written after that bump (SH-M048's
    /// mechanism, extended by TASK-184): a concurrent read that fetched the OLD value from L2 then either sees the
    /// bump and withdraws its backfill, or backfills before the bump and is overwritten by the L1 write after it.
    /// Before, such a read could leave the old value in L1 over this write for <c>L1DefaultExpiration</c>. With
    /// <see cref="HybridCacheOptions.WriteThrough"/> the two tiers are still written together, so L1 is written a
    /// second time after the bump.
    /// </remarks>
    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CacheEntryOptions.Validate(options); // SH-L041: a non-positive TTL is refused on every backend
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
            catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
            {
                // Fallback enabled: tolerate the L2 failure (L1 still observed in finally).
            }
            finally
            {
                await l1Task;
            }

            Interlocked.Increment(ref _generations[Stripe(key)]);
            await _l1.SetAsync(key, value, l1Options, ct);
        }
        else
        {
            // Write L2 first for durability, then L1
            try
            {
                await _l2.SetAsync(key, value, options, ct);
            }
            catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
            {
                // L2 failed, still populate L1
            }

            Interlocked.Increment(ref _generations[Stripe(key)]);
            await _l1.SetAsync(key, value, l1Options, ct);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        // L2 first, then the generation, then L1 (SH-M048). Removing both at once let a concurrent read miss
        // the cleared L1, read the value still in L2 and write it back into L1 for L1DefaultExpiration. In this
        // order a read either finds L2 already empty or backfills L1 under the old generation, and that
        // backfill is removed by the L1 removal below or withdrawn by the read itself (BackfillL1Async).
        try
        {
            await _l2.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
        {
            // L2 unavailable: the local copy is still removed below.
        }
        finally
        {
            Interlocked.Increment(ref _generations[Stripe(key)]);
            // Uncancellable on purpose: dropping a local copy is always safe, and a cancelled caller must not
            // leave L1 serving a value whose L2 removal already went through.
            await _l1.RemoveAsync(key, CancellationToken.None);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (await _l1.ExistsAsync(key, ct))
            return true;

        try
        {
            return await _l2.ExistsAsync(key, ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
        {
            return false;
        }
    }

    /// <remarks>
    /// A cached null is a hit (CR-L036): it is returned as-is and the factory does not run, whatever the nullability
    /// of <typeparamref name="T"/>. Use a nullable <typeparamref name="T"/> where a null may be cached (SH-L047).
    /// </remarks>
    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CacheEntryOptions.Validate(options); // SH-L041: a non-positive TTL is refused on every backend
        // Check L1
        var l1Result = await _l1.GetAsync<T>(key, ct);
        if (l1Result.HasValue)
            return l1Result.Value!;

        // Check L2
        var generation = CaptureGeneration(key);
        var l2Hit = CacheResult<T>.Miss();
        try
        {
            l2Hit = await _l2.GetAsync<T>(key, ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
        {
            // L2 unavailable, proceed to factory
        }
        if (l2Hit.HasValue)
        {
            await BackfillL1Async(key, l2Hit.Value, GetL1Options(options), generation, ct);
            return l2Hit.Value!;
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
        ThrowIfDisposed();
        // Same order as RemoveAsync (SH-M048), with the epoch standing in for every stripe.
        try
        {
            await _l2.RemoveByPrefixAsync(prefix, ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
        {
            // L2 unavailable: the local copies are still removed below.
        }
        finally
        {
            Interlocked.Increment(ref _epoch);
            await _l1.RemoveByPrefixAsync(prefix, CancellationToken.None);
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        // Same order as RemoveAsync (SH-M048), with the epoch standing in for every stripe.
        try
        {
            await _l2.ClearAsync(ct);
        }
        catch (Exception ex) when (IsL2FailureToAbsorb(ex, ct))
        {
            // L2 unavailable: the local copies are still removed below.
        }
        finally
        {
            Interlocked.Increment(ref _epoch);
            await _l1.ClearAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Whether an L2 failure is absorbed under <see cref="HybridCacheOptions.FallbackToL1OnL2Failure"/>. The
    /// caller's own cancellation never is (SH-M037): absorbed, a cancelled read looked like a miss and a
    /// cancelled Remove or Clear reported success without having run.
    /// </summary>
    private bool IsL2FailureToAbsorb(Exception ex, CancellationToken ct) =>
        _options.FallbackToL1OnL2Failure && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private static int Stripe(string key) => (key.GetHashCode() & int.MaxValue) % 256;

    private (int Epoch, int Stripe) CaptureGeneration(string key) =>
        (Volatile.Read(ref _epoch), Volatile.Read(ref _generations[Stripe(key)]));

    /// <summary>
    /// Writes an L2 hit into L1, then withdraws it if a removal ran since <paramref name="generation"/> was
    /// captured (before the L2 read). Checking after the write is what closes the window: a removal whose
    /// L1 removal came first bumped the generation before it, so the check sees the change (SH-M048).
    /// </summary>
    private async Task BackfillL1Async<T>(string key, T value, CacheEntryOptions l1Options, (int Epoch, int Stripe) generation, CancellationToken ct)
    {
        if (CaptureGeneration(key) != generation)
            return;
        await _l1.SetAsync(key, value, l1Options, ct);
        if (CaptureGeneration(key) != generation)
            await _l1.RemoveAsync(key, CancellationToken.None);
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
            // SH-L038: the cap applies here too. This is the L2-backfill path, and it returned L1DefaultExpiration
            // even when that exceeded the documented hard cap.
            return CacheEntryOptions.Absolute(maxExpiry.HasValue && maxExpiry.Value < _options.L1DefaultExpiration
                ? maxExpiry.Value
                : _options.L1DefaultExpiration);
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

    /// <summary>
    /// Marks the cache disposed: every later operation throws <see cref="ObjectDisposedException"/> (SH-L043).
    /// </summary>
    /// <remarks>
    /// SH-L044: the stampede locks are left alone. Disposing them here made a caller still inside
    /// <see cref="GetOrSetAsync{T}"/> throw <see cref="ObjectDisposedException"/> from its <c>Release</c>, masking the
    /// factory's result; each lock is disposed by its last releaser instead, and no new caller can take one. The
    /// caches are not disposed either: the caller owns L1 and L2.
    /// </remarks>
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
