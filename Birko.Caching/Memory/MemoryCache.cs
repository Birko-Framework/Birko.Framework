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
    public MemoryCache(TimeSpan? cleanupInterval = null)
    {
        var interval = cleanupInterval ?? TimeSpan.FromSeconds(60);
        _cleanupTimer = new Timer(_ => EvictExpired(), null, interval, interval);
    }

    public Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                _entries.TryRemove(key, out _);
                return Task.FromResult(CacheResult<T>.Miss());
            }

            entry.LastAccessedAt = DateTime.UtcNow;
            // Degrade a type mismatch to a Miss rather than throwing InvalidCastException on the
            // unchecked (T)entry.Value! cast (a shared key read with different T is a real foot-gun
            // for a general-purpose cache). A stored null is a legitimate hit (CR-L036).
            if (entry.Value is null)
                return Task.FromResult(CacheResult<T>.Hit(default!));
            return Task.FromResult(entry.Value is T typed
                ? CacheResult<T>.Hit(typed)
                : CacheResult<T>.Miss());
        }

        return Task.FromResult(CacheResult<T>.Miss());
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var entry = new MemoryCacheEntry(value, options ?? CacheEntryOptions.Default);
        _entries[key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                _entries.TryRemove(key, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        var result = await GetAsync<T>(key, ct);
        if (result.HasValue)
            return result.Value!;

        // Per-key lock to prevent cache stampede. Acquire a live (non-removed) KeyLock, retrying if
        // we happened to grab one that a concurrent last-releaser is retiring.
        var keyLock = AcquireKeyLock(key);
        await keyLock.Semaphore.WaitAsync(ct);
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
        ct.ThrowIfCancellationRequested();
        var keysToRemove = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));
        foreach (var key in keysToRemove)
            _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
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
        foreach (var kvp in _entries)
        {
            if (kvp.Value.IsExpired() && kvp.Value.Options.Priority != CachePriority.NeverRemove)
                _entries.TryRemove(kvp.Key, out _);
        }

        // Note: per-key locks are NOT evicted here. Removing a lock the timer merely observes as
        // idle raced with GetOrSetAsync (a caller between GetOrAdd and WaitAsync), letting two
        // callers run the factory concurrently (CR-M030). Locks are now refcounted and retired by
        // their last releaser instead (see ReleaseKeyLock).
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();

        foreach (var kvp in _locks)
        {
            try { kvp.Value.Semaphore.Dispose(); } catch (ObjectDisposedException) { }
        }
        _locks.Clear();
        _entries.Clear();
    }
}
