using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Caching.Memory;

/// <summary>
/// In-memory ICache implementation using ConcurrentDictionary.
/// Includes background cleanup of expired entries.
/// </summary>
public sealed class MemoryCache : ICache
{
    private readonly ConcurrentDictionary<string, MemoryCacheEntry> _entries = new();
    private readonly ConcurrentDictionary<string, KeyLock> _locks = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeProvider _clock;
    private volatile bool _disposed;

    /// <summary>
    /// Reference-counted per-key lock. A lock is only removed by its last releaser (Refs back to 0)
    /// under its own monitor, and a caller that observes a Removed lock retries with a fresh one —
    /// so a lock can never be recycled from under a caller between acquisition and use (CR-M030).
    /// </summary>
    private sealed class KeyLock
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Refs;
        public bool Removed;
    }

    /// <param name="cleanupInterval">Interval between expired entry evictions. Default: 60 seconds.</param>
    /// <param name="clock">
    /// The clock expiry is measured on; <see cref="TimeProvider.System"/> by default. SH-L052: a test drives expiry
    /// with a fake one. <see cref="TimeProvider"/> (BCL) rather than Birko.Time.Abstractions' IDateTimeProvider, so this
    /// project keeps its single dependency and no aggregator has to import another project for it.
    /// </param>
    public MemoryCache(TimeSpan? cleanupInterval = null, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        var interval = cleanupInterval ?? TimeSpan.FromSeconds(60);
        _cleanupTimer = new Timer(_ => EvictExpired(), null, interval, interval);
    }

    public Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                RemoveIfUnchanged(key, entry);
                return Task.FromResult(CacheResult<T>.Miss());
            }

            entry.Touch();
            // Degrade a type mismatch to a Miss rather than throwing InvalidCastException on the
            // unchecked (T)entry.Value! cast (a shared key read with different T is a real foot-gun
            // for a general-purpose cache). A stored null is a legitimate hit (CR-L036) — but only for a
            // T the null was stored as: a null has no runtime type to test, so the entry remembers the
            // T it was written with, and a read as an unrelated T is a Miss like any other mismatch
            // (SH-M039: a null cached by a single-entity read answered a List<T> read as "no rows").
            if (entry.Value is null)
                return Task.FromResult(typeof(T).IsAssignableFrom(entry.StoredType)
                    ? CacheResult<T>.Hit(default!)
                    : CacheResult<T>.Miss());
            return Task.FromResult(entry.Value is T typed
                ? CacheResult<T>.Hit(typed)
                : CacheResult<T>.Miss());
        }

        return Task.FromResult(CacheResult<T>.Miss());
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CacheEntryOptions.Validate(options, nameof(options));
        ct.ThrowIfCancellationRequested();
        var entry = new MemoryCacheEntry(value, typeof(T), options ?? CacheEntryOptions.Default, _clock);
        _entries[key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                RemoveIfUnchanged(key, entry);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    /// <remarks>A cached null is returned as-is without running the factory; see <see cref="ICache.GetOrSetAsync{T}"/> (SH-L047).</remarks>
    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        // Refused before anything runs, so a bad TTL never costs a factory call (SH-L041).
        CacheEntryOptions.Validate(options, nameof(options));
        var result = await GetAsync<T>(key, ct);
        if (result.HasValue)
            return result.Value!;

        // Per-key lock to prevent cache stampede. Acquire a live (non-removed) KeyLock, retrying if
        // we happened to grab one that a concurrent last-releaser is retiring.
        var keyLock = AcquireKeyLock(key);
        try
        {
            // Inside the try: a wait that is cancelled (or meets a disposed semaphore) must still give its
            // reference back, or the lock is never retired and the CR-M030 bound is gone (SH-M038).
            await keyLock.Semaphore.WaitAsync(ct);
        }
        catch
        {
            ReleaseKeyLock(key, keyLock);
            throw;
        }

        try
        {
            // Double-check after acquiring lock
            result = await GetAsync<T>(key, ct);
            if (result.HasValue)
                return result.Value!;

            var value = await factory(ct);
            await SetAsync(key, value, options, ct);
            return value;
        }
        finally
        {
            keyLock.Semaphore.Release();
            ReleaseKeyLock(key, keyLock);
        }
    }

    private KeyLock AcquireKeyLock(string key)
    {
        while (true)
        {
            var keyLock = _locks.GetOrAdd(key, _ => new KeyLock());
            lock (keyLock)
            {
                if (!keyLock.Removed)
                {
                    keyLock.Refs++;
                    return keyLock;
                }
            }
            // The instance we got is being retired; loop to get/create a fresh one.
        }
    }

    private void ReleaseKeyLock(string key, KeyLock keyLock)
    {
        lock (keyLock)
        {
            keyLock.Refs--;
            if (keyLock.Refs == 0)
            {
                keyLock.Removed = true;
                _locks.TryRemove(new KeyValuePair<string, KeyLock>(key, keyLock));
                keyLock.Semaphore.Dispose();
            }
        }
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var keysToRemove = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));
        foreach (var key in keysToRemove)
            _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        _entries.Clear();
        return Task.CompletedTask;
    }

    private void EvictExpired()
    {
        // Don't run against a disposed cache — the timer callback can fire concurrently with Dispose
        // (CR-L035). EvictExpired only touches the thread-safe _entries now (per-key locks are no
        // longer swept here, CR-M030), so this guard is belt-and-suspenders hardening.
        if (_disposed) return;
        // SH-L039: an expired entry is removed whatever its priority. The sweep used to keep a NeverRemove entry the
        // read path had already stopped serving (it misses and removes any expired entry), so the entry was only
        // retained memory. Priority has no meaning here: nothing evicts under memory pressure (SH-L040).
        foreach (var kvp in _entries)
        {
            if (kvp.Value.IsExpired())
                RemoveIfUnchanged(kvp.Key, kvp.Value);
        }

        // Note: per-key locks are NOT evicted here. Removing a lock the timer merely observes as
        // idle raced with GetOrSetAsync (a caller between GetOrAdd and WaitAsync), letting two
        // callers run the factory concurrently (CR-M030). Locks are now refcounted and retired by
        // their last releaser instead (see ReleaseKeyLock).
    }

    /// <summary>
    /// Removes <paramref name="key"/> only while it still maps to <paramref name="observed"/>.
    /// </summary>
    /// <remarks>
    /// SH-L042: a reader or the sweep decides an entry is expired, then removes the key. Removing by key alone deleted
    /// whatever a concurrent SetAsync had written in between (a GetOrSetAsync's fresh result vanished). The key/value
    /// overload removes only the entry that was observed, as <see cref="ReleaseKeyLock"/> does for the lock map.
    /// </remarks>
    private void RemoveIfUnchanged(string key, MemoryCacheEntry observed)
        => _entries.TryRemove(new KeyValuePair<string, MemoryCacheEntry>(key, observed));

    // SH-L043: a disposed cache used to keep accepting writes with no sweep left to expire them.
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();

        // SH-L044: the per-key semaphores are not disposed here. A GetOrSetAsync caller inside its critical section
        // still releases its semaphore in a finally, and a disposed one threw ObjectDisposedException there, masking the
        // factory's result. Each lock is retired (and its semaphore disposed) by its last releaser, as always; one never
        // released holds no wait handle (SemaphoreSlim allocates one only for AvailableWaitHandle), so nothing leaks.
        _locks.Clear();
        _entries.Clear();
    }
}
