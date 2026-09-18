using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Caching;

/// <summary>
/// Unified caching interface for all cache backends (memory, Redis, hybrid).
/// </summary>
public interface ICache : IDisposable
{
    Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets a value from cache, or creates it using the factory and caches the result.
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Removes all entries matching the prefix.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Removes all entries from the cache — <b>the cache, not the store behind it</b>.
    /// <para>
    /// An implementation that cannot tell its own entries apart from a shared backing store's must refuse
    /// rather than widen the operation to everything it can reach (SH-H006). <c>RedisCache</c> throws
    /// <c>WholeDatabaseDeleteException</c> when no <c>KeyPrefix</c> gives it a namespace of its own.
    /// </para>
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}
