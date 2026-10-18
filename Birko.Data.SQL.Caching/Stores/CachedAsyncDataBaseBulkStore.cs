using Birko.Caching;
using Birko.Data.SQL.Caching;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
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
        /// Identity of the database this store is pointed at, used as the outermost cache-key segment.
        /// </summary>
        /// <remarks>
        /// SH-H005. Read <b>lazily</b> rather than in the constructor, and that is forced rather than
        /// stylistic: <see cref="ResolveTableName"/> deliberately runs before <c>SetSettings</c>
        /// (CR-L177) because a table name depends only on <c>T</c>'s mapping — but the database identity
        /// does not exist until <c>SetSettings</c> has produced a <c>Connector</c>. So the key can no
        /// longer be composed entirely at construction.
        /// <para>
        /// The <c>"_"</c> fallback is for a store whose settings were never applied. Such a store cannot
        /// read anything (the base needs the connector), so the value is unreachable in a working flow —
        /// it exists so an unconfigured store produces a deterministic key rather than throwing from a
        /// key builder, which would report the wrong defect.
        /// </para>
        /// </remarks>
        private string CacheScope => Connector?.Settings?.GetId() ?? "_";

        /// <summary>
        /// Set while a read-then-write flow is in progress, so reads inside it go straight to SQL.
        /// </summary>
        /// <remarks>
        /// SH-H007. <see cref="AsyncLocal{T}"/> rather than a plain field, and an <b>instance</b> field
        /// rather than a static one: the flag is per call flow <i>and</i> per store, which is the same
        /// mechanism TASK-270 established for <c>DataBase.IsInitializing</c> after a plain mutable bool
        /// on a shared object turned out to be a race. A store registered as a singleton serves concurrent
        /// requests, so a plain field here would let one request's update disable another request's cache
        /// — or worse, leave it disabled.
        /// </remarks>
        private readonly AsyncLocal<bool> _bypassReadCache = new();

        /// <summary>
        /// Enters a scope in which this store's reads bypass the cache, restoring the previous value on
        /// dispose — so nesting works and an exception cannot leave the flag stuck on.
        /// </summary>
        private IDisposable BypassReadCache() => new ReadCacheBypass(this);

        private sealed class ReadCacheBypass : IDisposable
        {
            private readonly CachedAsyncDataBaseBulkStore<DB, T> _store;
            private readonly bool _previous;

            public ReadCacheBypass(CachedAsyncDataBaseBulkStore<DB, T> store)
            {
                _store = store;
                _previous = store._bypassReadCache.Value;
                store._bypassReadCache.Value = true;
            }

            // Restores rather than clearing: a nested flow must not switch the cache back on for its
            // caller. TASK-270 recorded the opposite mistake — an assignment pair that left a guard stuck.
            public void Dispose() => _store._bypassReadCache.Value = _previous;
        }

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

        /// <summary>
        /// Serializer options for the cached form of a row. Not <c>CacheSerializer</c>'s: those drop a null
        /// property, and a model whose property has an initializer would then read back the initializer's value
        /// where the row held NULL.
        /// </summary>
        private static readonly JsonSerializerOptions CachedRowJson = new();

        /// <summary>
        /// Whether every mapped column survives the JSON round-trip the cache stores (a public getter and setter).
        /// </summary>
        /// <remarks>
        /// SH-M047: the cache now holds a JSON copy, never the entity or list a caller was handed. A mapped
        /// property the serializer cannot write back would come out of the cache silently reset, so a type with one
        /// is not cached at all (rule 41: refuse rather than drop). Read lazily, for the same reason as
        /// <see cref="CacheScope"/>: the mapping may be registered after the store is constructed.
        /// </remarks>
        private bool RowsRoundTrip => _rowsRoundTrip ??= ComputeRowsRoundTrip();
        private bool? _rowsRoundTrip;

        private static bool ComputeRowsRoundTrip()
        {
            var table = SQL.DataBase.TryLoadTable(typeof(T));
            if (table?.Fields == null) return false;
            return table.Fields.Values.All(f => f.Property?.GetGetMethod() != null && f.Property.GetSetMethod() != null);
        }

        /// <summary>
        /// Whether this call flow is inside a transaction on this store's database: the store's own transaction
        /// context or an ambient one (a <c>SqlUnitOfWork</c>, a bulk write's own boundary).
        /// </summary>
        /// <remarks>
        /// SH-M044: a read inside a transaction may see that transaction's uncommitted rows, and the cache key has
        /// no session identity, so caching it served rows to every other caller that a rollback then erased. Such a
        /// read neither consults nor fills the cache. The same test <c>AtomicallyAsync</c> uses decides it.
        /// </remarks>
        private bool InTransaction
            => Connector != null
               && (TransactionContext != null || AmbientSqlTransaction.Find(Connector.Settings.GetId()) != null);

        private bool UseReadCache => _options.Enabled && !_bypassReadCache.Value && !InTransaction && RowsRoundTrip;

        /// <inheritdoc />
        /// <remarks>
        /// SH-M035: a row that is not found is cached as the JSON <c>null</c>, so a backend that refuses to store a
        /// null value (Redis, a Redis-backed hybrid) caches the miss instead of throwing out of the read.
        /// </remarks>
        protected override async Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            if (!UseReadCache)
            {
                return await base.ReadCoreAsync(filter, ct);
            }

            // SH-H004: a raw filter.ToString() is not a value-distinguishing description of the query —
            // a captured local renders identically for every value. TryDescribeFilter funcletizes and then
            // CHECKS the rendering; when it cannot be keyed we bypass the cache completely rather than
            // reuse a key that means something else.
            if (!SqlCacheKeyBuilder.TryDescribeFilter(filter, out var filterString))
            {
                return await base.ReadCoreAsync(filter, ct);
            }

            var key = SqlCacheKeyBuilder.BuildSingleKey(CacheScope, _tableName, filterString, typeof(T));

            var cached = await _cache.GetAsync<string>(key, ct);
            if (cached.HasValue && cached.Value != null)
            {
                return JsonSerializer.Deserialize<T>(cached.Value, CachedRowJson);
            }

            var result = await base.ReadCoreAsync(filter, ct);

            await _cache.SetAsync(key, JsonSerializer.Serialize(result, CachedRowJson), CreateEntryOptions(), ct);

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
            if (!UseReadCache)
            {
                return await base.ReadCoreAsync(filter, orderBy, limit, offset, ct);
            }

            // SH-H004 — see the single-result overload above; the same rule applies to both read paths,
            // and it is the same producer that decides.
            if (!SqlCacheKeyBuilder.TryDescribeFilter(filter, out var filterString))
            {
                return await base.ReadCoreAsync(filter, orderBy, limit, offset, ct);
            }

            var orderString = orderBy?.ToDictionary() is { } dict
                ? string.Join(",", dict.Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                : null;
            var key = SqlCacheKeyBuilder.BuildKey(CacheScope, _tableName, filterString, orderString, limit, offset, typeof(T));

            var cached = await _cache.GetAsync<string>(key, ct);
            if (cached.HasValue && cached.Value != null)
            {
                return JsonSerializer.Deserialize<List<T>>(cached.Value, CachedRowJson) ?? new List<T>();
            }

            var result = (await base.ReadCoreAsync(filter, orderBy, limit, offset, ct)).ToList();

            await _cache.SetAsync(key, JsonSerializer.Serialize(result, CachedRowJson), CreateEntryOptions(), ct);

            return result;
        }

        #endregion

        #region Write Operations with Cache Invalidation

        /// <inheritdoc />
        protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            var result = Guid.Empty;
            await WriteThenInvalidateAsync(async () => result = await base.CreateCoreAsync(data, processDelegate, ct));
            return result;
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.UpdateCoreAsync(data, processDelegate, ct));
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.DeleteCoreAsync(data, ct));
        }

        /// <inheritdoc />
        protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.CreateCoreAsync(data, storeDelegate, ct));
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.UpdateCoreAsync(data, storeDelegate, ct));
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.DeleteCoreAsync(data, ct));
        }

        /// <inheritdoc />
        /// <remarks>
        /// SH-M046: a dropped table's rows used to stay cached, because DROP TABLE runs through this seam and this
        /// class did not override it.
        /// </remarks>
        protected override async Task DestroyCoreAsync(CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.DestroyCoreAsync(ct));
        }

        // The filter-based native write methods below bypass the *Core template (they issue the write
        // straight through the connector), so overriding only the *Core methods left them without cache
        // invalidation — a filter Update/Delete produced stale cached reads until TTL expiry (CR-C16).
        //
        // ⚠ The UpdateAsync(filter, Action<T>) overload invalidates correctly (it loops per-item through
        // UpdateAsync -> UpdateCoreAsync) — but invalidation was never its problem. That comment used to
        // say it was "already safe", full stop, which is the sentence that stopped anyone looking: the
        // overload READS through the cache before it writes. See its override below (SH-H007).

        /// <inheritdoc />
        /// <remarks>
        /// SH-H007 — this overload is read-then-write, and the read must not come from the cache.
        /// <para>
        /// <c>AsyncDataBaseBulkStore</c>'s implementation is <c>ReadAsync(filter, …)</c> followed by a
        /// per-item <c>UpdateAsync(item)</c>, and that read dispatches to this class's caching
        /// <c>ReadCoreAsync</c>. So the loop mutated entities as they were up to
        /// <c>SqlCacheOptions.DefaultExpiration</c> ago and <c>UpdateCoreAsync</c> issued a full-row
        /// UPDATE of <b>every mapped column</b> — silently overwriting whatever another writer had
        /// changed in the meantime. Lost updates with no error.
        /// </para>
        /// <para>
        /// <b>The fix changes only where that read comes from.</b> It does <i>not</i> re-implement the
        /// guard or the loop: <c>base.UpdateAsync</c> still runs, so <c>RequireFilter</c> (which is
        /// <c>private</c> to that class and therefore unreachable from here anyway) and the per-item
        /// update — which invalidates — are untouched. Only the read inside it is diverted, by a
        /// flow-scoped flag this class's <c>ReadCoreAsync</c> honours. Re-implementing the loop would have
        /// duplicated a rule that already has one producer.
        /// </para>
        /// <para>
        /// ⚠ Not solved by shortening the TTL: any non-zero window is a lost update, and a window small
        /// enough to be safe would make the cache pointless. The read simply must not be cached.
        /// </para>
        /// <para>
        /// ⚠ The flag also covers any read <paramref name="updateAction"/> itself performs on this store
        /// during the loop. That is wider than strictly necessary and deliberately so: an uncached read is
        /// always correct, so the over-reach costs performance and cannot cost correctness.
        /// </para>
        /// </remarks>
        public override async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
        {
            using (BypassReadCache())
            {
                await base.UpdateAsync(filter, updateAction, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.UpdateAsync(filter, updates, ct));
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        {
            await WriteThenInvalidateAsync(() => base.DeleteAsync(filter, ct));
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Runs a write, then invalidates this table's cached reads — also when the write threw.
        /// </summary>
        /// <remarks>
        /// SH-M045: invalidation used to take the caller's token, so a token cancelled after the row was written
        /// skipped it (both caches check the token first) and the table's cached reads served the pre-write rows
        /// until they expired. Once a write may have reached the table, invalidation is not cancellable: it runs with
        /// <see cref="CancellationToken.None"/>. A write that threw may still have changed rows (a bulk write outside
        /// a transaction stops part-way), so it invalidates too; an invalidation failure there is swallowed, because
        /// the write's own exception is the one the caller needs. Invalidating more than necessary costs a miss.
        /// <para>
        /// Inside a transaction that first invalidation runs before the commit, so a reader outside the transaction
        /// could cache the pre-commit rows in between, and that entry survived the commit for the whole TTL (SH-M044).
        /// TASK-669: the write also registers a second invalidation to run once the boundary commits
        /// (<see cref="AmbientSqlTransaction.OnCommitted"/>), run by its owner — <c>SqlUnitOfWork</c> or a bulk
        /// write's own transaction — and dropped on rollback. ⚠ A caller that commits the <c>DbTransaction</c> it
        /// handed in through <c>SetTransactionContext</c> itself, without a <c>SqlUnitOfWork</c>, has no owner to run
        /// it, so that path keeps the pre-commit window.
        /// </para>
        /// </remarks>
        private async Task WriteThenInvalidateAsync(Func<Task> write)
        {
            InvalidateAgainAfterCommit();
            try
            {
                await write().ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await InvalidateCacheAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Deliberately swallowed: see remarks.
                }
                throw;
            }
            await InvalidateCacheAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Registers this table's invalidation to run after the enclosing transaction commits, when the write runs
        /// inside one (TASK-669). Keyed per store instance, so many writes in one boundary invalidate once, and two
        /// stores over different caches each invalidate their own.
        /// </summary>
        private void InvalidateAgainAfterCommit()
        {
            if (Connector == null) return;
            var transaction = TransactionContext?.Transaction ?? AmbientSqlTransaction.Find(Connector.Settings.GetId())?.Transaction;
            if (transaction == null) return;
            AmbientSqlTransaction.OnCommitted(transaction, _afterCommitKey, () => InvalidateCacheAsync());
        }

        private readonly string _afterCommitKey = "birko-sql-cache:" + Guid.NewGuid().ToString("N");

        // SH-L054: no Enabled check. A store with caching disabled still writes the table, and another store over
        // the same cache and table with caching enabled must not go on serving the rows it changed.
        private async Task InvalidateCacheAsync()
        {

            // SH-H005: the prefix carries the same scope the keys do. The two MUST move together — a
            // scoped key with an unscoped prefix would over-invalidate (harmless), but an unscoped key
            // with a scoped prefix would leave entries nothing ever removes, which is worse than the
            // cross-database leak being fixed.
            var prefix = SqlCacheKeyBuilder.GetTablePrefix(CacheScope, _tableName);
            await _cache.RemoveByPrefixAsync(prefix, CancellationToken.None);
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
            var table = SQL.DataBase.TryLoadTable(typeof(T));
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
