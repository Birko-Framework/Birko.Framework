using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Caching.Serialization;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// TASK-169 on the SQL cached store: SH-M035 (a not-found single read threw on a cache that refuses null),
/// SH-M036 (the single read and a limit-1 collection read shared a key), SH-M044 (rows read inside a transaction were
/// cached for everyone), SH-M045 (a cancelled token skipped invalidation after a durable write), SH-M046 (DROP TABLE
/// left the table's reads cached) and SH-M047 (the cache handed every caller the same entity and list instances).
/// Part of <see cref="CachedStoreBehaviorTests"/> so the global table registration is applied by one fixture.
/// </summary>
public partial class CachedStoreBehaviorTests
{
    /// <summary>
    /// A cache that stores bytes the way <c>RedisCache</c> does: <see cref="CacheSerializer"/> on the way in (which
    /// refuses null), deserialization to the requested type on the way out.
    /// </summary>
    private sealed class SerializingCache : ICache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();
        public CancellationToken LastInvalidationToken { get; private set; }

        public Task<CacheResult<TValue>> GetAsync<TValue>(string key, CancellationToken ct = default)
            => Task.FromResult(_entries.TryGetValue(key, out var bytes)
                ? CacheResult<TValue>.Hit(CacheSerializer.Deserialize<TValue>(bytes)!)
                : CacheResult<TValue>.Miss());

        public Task SetAsync<TValue>(string key, TValue value, CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            _entries[key] = CacheSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) { _entries.TryRemove(key, out _); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(_entries.ContainsKey(key));
        public Task<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            LastInvalidationToken = ct;
            ct.ThrowIfCancellationRequested(); // as MemoryCache and RedisCache do
            foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _entries.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default) { _entries.Clear(); return Task.CompletedTask; }
        public void Dispose() { }
    }

    private CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget> NewStore(ICache cache)
    {
        var store = new CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>(cache, new SqlCacheOptions { Enabled = true });
        store.SetSettings(_settings);
        return store;
    }

    [Fact]
    public async Task SH_M035_a_not_found_single_read_is_cached_on_a_cache_that_refuses_null()
    {
        var store = NewStore(new SerializingCache());
        await store.InitAsync();
        Expression<Func<Widget, bool>> filter = x => x.Name == "nobody";

        var first = async () => await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter);
        (await first.Should().NotThrowAsync()).Subject.Should().BeNull();

        // The miss is cached: a row added out of band is not seen until a write through the store invalidates.
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using (var conn = connector.CreateConnection(connector.Settings))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO Widgets (Guid, Name) VALUES ('{Guid.NewGuid()}', 'nobody')";
            cmd.ExecuteNonQuery();
        }
        (await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter)).Should().BeNull("the not-found result was cached");
    }

    [Fact]
    public async Task SH_M036_a_single_read_and_a_limit_1_collection_read_do_not_share_an_entry()
    {
        var store = NewStore(new SerializingCache());
        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        var single = await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter);
        single!.Name.Should().Be("a");

        var list = async () => (await store.ReadAsync(filter, null, 1, null)).ToList();
        (await list.Should().NotThrowAsync()).Subject.Should().ContainSingle().Which.Name.Should().Be("a");
        var again = async () => await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter);
        (await again.Should().NotThrowAsync()).Subject!.Name.Should().Be("a");
    }

    [Fact]
    public void SH_M036_the_single_key_differs_from_every_collection_key()
    {
        SqlCacheKeyBuilder.BuildSingleKey("s", "t", "f").Should().NotBe(SqlCacheKeyBuilder.BuildKey("s", "t", "f", null, 1, null));
        SqlCacheKeyBuilder.BuildSingleKey("s", "t", "f").Should().StartWith(SqlCacheKeyBuilder.GetTablePrefix("s", "t"));
    }

    [Fact]
    public async Task SH_M044_a_read_inside_a_rolled_back_transaction_is_not_cached()
    {
        var store = NewStore(new MemoryCache());
        await store.InitAsync();
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        Expression<Func<Widget, bool>> filter = x => x.Name == "uncommitted";

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);
            await store.CreateAsync(new Widget { Name = "uncommitted" });
            (await store.ReadAsync(filter)).Should().ContainSingle("the transaction sees its own row");
            await uow.RollbackAsync();
            store.SetTransactionContext(null);
        }

        (await store.ReadAsync(filter)).Should().BeEmpty("the rolled-back row was never committed, so no one may be served it");
    }

    [Fact]
    public async Task SH_M044_a_read_inside_an_ambient_transaction_is_not_cached()
    {
        var store = NewStore(new MemoryCache());
        await store.InitAsync();
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        Expression<Func<Widget, bool>> filter = x => x.Name == "uncommitted";

        using (AmbientSqlTransaction.InstallCell())
        {
            await using var uow = new SqlUnitOfWork(connector, _settings);
            await uow.BeginAsync();
            await store.CreateAsync(new Widget { Name = "uncommitted" });
            (await store.ReadAsync(filter)).Should().ContainSingle();
            await uow.RollbackAsync();
        }

        (await store.ReadAsync(filter)).Should().BeEmpty();
    }

    [Fact]
    public async Task SH_M045_invalidation_after_a_write_cannot_be_cancelled()
    {
        var cache = new SerializingCache();
        var store = NewStore(cache);
        await store.InitAsync();
        using var cts = new CancellationTokenSource();

        await store.CreateAsync(new Widget { Name = "a" }, null, cts.Token);

        cache.LastInvalidationToken.CanBeCanceled.Should().BeFalse(
            "once the row may be written, a cancelled token must not skip invalidation (both caches check it first)");
    }

    [Fact]
    public async Task SH_M046_destroying_the_table_invalidates_its_cached_reads()
    {
        var store = NewStore(new MemoryCache());
        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";
        (await store.ReadAsync(filter)).Should().HaveCount(1);

        await store.DestroyAsync();

        (await store.ReadAsync(filter)).Should().BeEmpty("the table was dropped (and re-created empty by the next read)");
    }

    [Fact]
    public async Task SH_M047_a_caller_mutating_a_cached_result_does_not_change_what_others_read()
    {
        var store = NewStore(new MemoryCache());
        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "original" } });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        var first = await store.ReadAsync(filter);
        first.Single().Body = "changed by one caller";
        if (first is List<Widget> list) list.Clear();

        var second = await store.ReadAsync(filter);
        second.Should().ContainSingle().Which.Body.Should().Be("original");

        var one = await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter);
        one!.Body = "changed";
        (await ((Birko.Data.Stores.IAsyncReadStore<Widget>)store).ReadAsync(filter))!.Body.Should().Be("original");
    }
}
