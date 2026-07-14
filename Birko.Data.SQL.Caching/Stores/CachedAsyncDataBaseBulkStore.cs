using Birko.Caching;
using Birko.Data.SQL.Caching;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Async bulk database store with transparent query caching.
    /// Caches ReadAsync results and automatically invalidates on writes.
    /// </summary>
    /// <typeparam name="DB">The type of database connector, must inherit from <see cref="AbstractConnector"/>.</typeparam>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class CachedAsyncDataBaseBulkStore<DB, T> : AsyncDataBaseBulkStore<DB, T>
        where T : Models.AbstractModel
        where DB : AbstractConnector
    {
        private readonly ICache _cache;
        private readonly SqlCacheOptions _options;
        private readonly string _tableName;

        /// <summary>
        /// Initializes a new instance of the CachedAsyncDataBaseBulkStore class.
        /// </summary>
        /// <param name="cache">The cache implementation to use.</param>
        /// <param name="options">Optional cache configuration. Uses defaults if null.</param>
        public CachedAsyncDataBaseBulkStore(ICache cache, SqlCacheOptions? options = null)
            : base()
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? new SqlCacheOptions();
            _tableName = ResolveTableName();
        }

        #region Cached Read Operations

        /// <inheritdoc />
        protected override async Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                return await base.ReadCoreAsync(filter, ct);
            }

            var filterString = filter?.ToString();
            var key = SqlCacheKeyBuilder.BuildKey(_tableName, filterString, null, 1, null);

            var cached = await _cache.GetAsync<T>(key, ct);
            if (cached.HasValue)
            {
                return cached.Value;
            }

            var result = await base.ReadCoreAsync(filter, ct);

            await _cache.SetAsync(key, result, CreateEntryOptions(), ct);

            return result;
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                return await base.ReadCoreAsync(filter, orderBy, limit, offset, ct);
            }

            var filterString = filter?.ToString();
            var orderString = orderBy?.ToDictionary() is { } dict
                ? string.Join(",", dict.Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                : null;
            var key = SqlCacheKeyBuilder.BuildKey(_tableName, filterString, orderString, limit, offset);

            var cached = await _cache.GetAsync<List<T>>(key, ct);
            if (cached.HasValue)
            {
                return cached.Value ?? Enumerable.Empty<T>();
            }

            var result = (await base.ReadCoreAsync(filter, orderBy, limit, offset, ct)).ToList();

            await _cache.SetAsync(key, result, CreateEntryOptions(), ct);

            return result;
        }

        #endregion

        #region Write Operations with Cache Invalidation

        /// <inheritdoc />
        protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            var result = await base.CreateCoreAsync(data, processDelegate, ct);
            await InvalidateCacheAsync(ct);
            return result;
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await base.UpdateCoreAsync(data, processDelegate, ct);
            await InvalidateCacheAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            await base.DeleteCoreAsync(data, ct);
            await InvalidateCacheAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await base.CreateCoreAsync(data, storeDelegate, ct);
            await InvalidateCacheAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await base.UpdateCoreAsync(data, storeDelegate, ct);
            await InvalidateCacheAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            await base.DeleteCoreAsync(data, ct);
            await InvalidateCacheAsync(ct);
        }

        // The filter-based native write methods below bypass the *Core template (they issue the write
        // straight through the connector), so overriding only the *Core methods left them without cache
        // invalidation — a filter Update/Delete produced stale cached reads until TTL expiry (CR-C16).
        // (The UpdateAsync(filter, Action<T>) overload is already safe: it loops per-item through
        // UpdateAsync -> UpdateCoreAsync, which invalidates.)

        /// <inheritdoc />
        public override async Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
        {
            await base.UpdateAsync(filter, updates, ct);
            await InvalidateCacheAsync(ct);
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        {
            await base.DeleteAsync(filter, ct);
            await InvalidateCacheAsync(ct);
        }

        #endregion

        #region Private Helpers

        private async Task InvalidateCacheAsync(CancellationToken ct)
        {
            if (!_options.Enabled) return;

            var prefix = SqlCacheKeyBuilder.GetTablePrefix(_tableName);
            await _cache.RemoveByPrefixAsync(prefix, ct);
        }

        private CacheEntryOptions CreateEntryOptions()
        {
            return CacheEntryOptions.Absolute(_options.DefaultExpiration);
        }

        // Resolved once at construction (see ctor): the table name depends only on T's mapping
        // attributes, not on connection/settings state, and LoadTable is static/cached — so resolving
        // it before SetSettings/Init is correct and cheap (CR-L177). It feeds the cache-key prefix only.
        private static string ResolveTableName()
        {
            var table = SQL.DataBase.LoadTable(typeof(T));
            if (table != null && !string.IsNullOrEmpty(table.Name))
            {
                return table.Name;
            }

            // Fallback to type name if table attribute is not found
            return typeof(T).Name;
        }

        #endregion
    }
}
