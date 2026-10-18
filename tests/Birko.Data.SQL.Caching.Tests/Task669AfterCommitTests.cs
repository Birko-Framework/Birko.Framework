using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// TASK-669: a write inside a transaction invalidated the table's cached reads before the commit, so a reader outside
/// the transaction could cache the committed (pre-write) rows in between, and that entry outlived the commit for the
/// whole TTL. The write now also invalidates after the commit, run by the boundary's owner. Part of
/// <see cref="CachedStoreBehaviorTests"/> so the global table registration is applied by one fixture.
/// </summary>
public partial class CachedStoreBehaviorTests
{
    /// <summary>A MemoryCache that counts prefix invalidations.</summary>
    private sealed class CountingCache : ICache
    {
        private readonly MemoryCache _inner = new();
        public int Invalidations;
        public Task<CacheResult<TValue>> GetAsync<TValue>(string key, CancellationToken ct = default) => _inner.GetAsync<TValue>(key, ct);
        public Task SetAsync<TValue>(string key, TValue value, CacheEntryOptions? options = null, CancellationToken ct = default) => _inner.SetAsync(key, value, options, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => _inner.ExistsAsync(key, ct);
        public Task<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) => _inner.GetOrSetAsync(key, factory, options, ct);
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) { Interlocked.Increment(ref Invalidations); return _inner.RemoveByPrefixAsync(prefix, ct); }
        public Task ClearAsync(CancellationToken ct = default) => _inner.ClearAsync(ct);
        public void Dispose() => _inner.Dispose();
    }

    private static readonly Expression<Func<Widget, bool>> ByNameA = x => x.Name == "a";

    /// <summary>A read from another async flow, outside any transaction: it sees the committed rows and fills the cache.</summary>
    private static Task<string?> ReadBodyOutside(CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget> store) => Task.Run(async () =>
    {
        using (AmbientSqlTransaction.Suppress())
        {
            return (await store.ReadAsync(ByNameA)).Single().Body;
        }
    });

    private async Task<(CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget> store, Widget row)> SeedAsync(ICache cache)
    {
        var store = NewStore(cache);
        var row = new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "old" };
        await store.CreateAsync(row);
        return (store, row);
    }

    [Fact]
    public async Task TASK_669_a_unit_of_work_commit_invalidates_what_an_outside_reader_cached_meanwhile()
    {
        var cache = new MemoryCache();
        var (store, row) = await SeedAsync(cache);
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            row.Body = "new";
            await store.UpdateAsync(row);
            (await ReadBodyOutside(store)).Should().Be("old", "the write is not committed yet, and this read caches it");
            await uow.CommitAsync();
        }

        (await ReadBodyOutside(store)).Should().Be("new", "the commit invalidated the entry cached before it");
    }

    [Fact]
    public async Task TASK_669_the_explicit_transaction_context_path_is_invalidated_after_commit_too()
    {
        var cache = new MemoryCache();
        var (writer, row) = await SeedAsync(cache);
        var reader = NewStore(cache); // another store on the same cache: the writer's TransactionContext is its own
        await reader.InitAsync(); // before the boundary: its lazy schema-ensure would wait on the writer's lock
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            writer.SetTransactionContext(uow.Context);
            row.Body = "new";
            await writer.UpdateAsync(row);
            (await ReadBodyOutside(reader)).Should().Be("old");
            await uow.CommitAsync();
            writer.SetTransactionContext(null);
        }

        (await ReadBodyOutside(reader)).Should().Be("new");
    }

    [Fact]
    public async Task TASK_669_a_rollback_runs_no_after_commit_work_and_the_cache_stays_right()
    {
        var cache = new CountingCache();
        var (store, row) = await SeedAsync(cache);
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            row.Body = "new";
            await store.UpdateAsync(row);
            (await ReadBodyOutside(store)).Should().Be("old");
            var before = cache.Invalidations;
            await uow.RollbackAsync();
            cache.Invalidations.Should().Be(before, "nothing committed, so nothing runs after");
        }

        (await ReadBodyOutside(store)).Should().Be("old");
    }

    [Fact]
    public async Task TASK_669_many_writes_in_one_boundary_invalidate_once_after_the_commit()
    {
        var cache = new CountingCache();
        var (store, row) = await SeedAsync(cache);
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            for (var i = 0; i < 3; i++)
            {
                row.Body = "v" + i;
                await store.UpdateAsync(row);
            }
            var beforeCommit = cache.Invalidations;
            await uow.CommitAsync();
            (cache.Invalidations - beforeCommit).Should().Be(1, "the after-commit work is registered once per store and runs once");
        }
    }

    [Fact]
    public async Task TASK_669_a_bulk_write_with_its_own_transaction_runs_the_after_commit_work()
    {
        var cache = new CountingCache();
        var store = NewStore(cache);
        await store.InitAsync();
        var before = cache.Invalidations;

        await store.CreateAsync(Enumerable.Range(0, 3).Select(i => new Widget { Guid = Guid.NewGuid(), Name = "bulk" + i }).ToList());

        // Three per-item invalidations inside the batch's transaction, one after its commit, one for the collection.
        (cache.Invalidations - before).Should().Be(5);
    }

    [Fact]
    public async Task TASK_669_a_failing_after_commit_callback_is_recorded_not_thrown()
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        await NewStore(new MemoryCache()).InitAsync();
        var ran = 0;

        await using (var uow = new SqlUnitOfWork(connector, _settings))
        {
            await uow.BeginAsync();
            AmbientSqlTransaction.OnCommitted(uow.Context!.Transaction, "boom", () => throw new InvalidOperationException("callback failed"));
            AmbientSqlTransaction.OnCommitted(uow.Context!.Transaction, "after", () => { ran++; return Task.CompletedTask; });
            var commit = async () => await uow.CommitAsync();
            await commit.Should().NotThrowAsync("the transaction committed; a callback cannot report it as failed");
        }

        ran.Should().Be(1, "one callback failing does not stop the next");
        connector.SubscriberFailures.Should().Contain(f => f.Channel == "AfterCommit" && f.Error is InvalidOperationException);
    }
}
