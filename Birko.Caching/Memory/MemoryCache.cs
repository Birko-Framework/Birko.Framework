using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <param name="cleanupInterval">Interval between expired entry evictions. Default: 60 seconds.</param>
    public MemoryCache(TimeSpan? cleanupInterval = null)
    {
        var interval = cleanupInterval ?? TimeSpan.FromSeconds(60);
        _cleanupTimer = new Timer(_ => EvictExpired(), null, interval, interval);
    }

    public Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                _entries.TryRemove(key, out _);
                return Task.FromResult(CacheResult<T>.Miss());
            }

            entry.LastAccessedAt = DateTime.UtcNow;
            return Task.FromResult(CacheResult<T>.Hit((T)entry.Value!));
        }

        return Task.FromResult(CacheResult<T>.Miss());
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        var entry = new MemoryCacheEntry(value, options ?? CacheEntryOptions.Default);
        _entries[key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
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

        // Per-key lock to prevent cache stampede
        var keyLock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
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
            keyLock.Release();
        }
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var keysToRemove = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));
        foreach (var key in keysToRemove)
            _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }

    private void EvictExpired()
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Value.IsExpired() && kvp.Value.Options.Priority != CachePriority.NeverRemove)
                _entries.TryRemove(kvp.Key, out _);
        }

        // Clean up unused locks
        foreach (var kvp in _locks)
        {
            if (!_entries.ContainsKey(kvp.Key) && kvp.Value.CurrentCount == 1)
                _locks.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();

        foreach (var kvp in _locks)
            kvp.Value.Dispose();
        _locks.Clear();
        _entries.Clear();
    }
}
