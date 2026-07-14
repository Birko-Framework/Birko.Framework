using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.Paging;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-L159: AsyncPagedRepositoryWrapper must not run its page-read and count concurrently on the same
/// repository instance (a connection-bound backend cannot service two in-flight calls). These tests
/// prove the two operations are awaited sequentially (observed max concurrency == 1) and that the
/// paged result is assembled correctly.
/// </summary>
public class AsyncPagedRepositoryWrapperTests
{
    private class Item : AbstractModel { }

    /// <summary>
    /// Instrumented fake: ReadAsync (bulk) and CountAsync increment a shared in-flight counter and
    /// yield, recording the maximum concurrency observed. Every other repository member is unused.
    /// </summary>
    private sealed class ConcurrencyProbeRepository : IAsyncBulkRepository<Item>
    {
        private int _inFlight;
        public int MaxConcurrency;
        public readonly List<Item> Items;
        public readonly long Total;

        public ConcurrencyProbeRepository(List<Item> items, long total)
        {
            Items = items;
            Total = total;
        }

        private async Task Enter()
        {
            var now = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref MaxConcurrency, now);
            await Task.Yield();
            await Task.Yield();
        }

        private void Leave() => Interlocked.Decrement(ref _inFlight);

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)))
            {
                if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
            }
        }

        public async Task<IEnumerable<Item>> ReadAsync(
            Expression<Func<Item, bool>>? filter = null, OrderBy<Item>? orderBy = null,
            int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            await Enter();
            try { return Items; }
            finally { Leave(); }
        }

        public async Task<long> CountAsync(Expression<Func<Item, bool>>? filter = null, CancellationToken ct = default)
        {
            await Enter();
            try { return Total; }
            finally { Leave(); }
        }

        // --- unused surface ---
        public Task DestroyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IEnumerable<Item>> ReadAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item?> ReadAsync(Guid guid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Item?> ReadAsync(Expression<Func<Item, bool>>? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Item data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateAsync(IEnumerable<Item> data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Item data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(IEnumerable<Item> data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Expression<Func<Item, bool>> filter, Action<Item> updateAction, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Expression<Func<Item, bool>> filter, PropertyUpdate<Item> updates, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Item data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(IEnumerable<Item> data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Expression<Func<Item, bool>> filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Item CreateInstance() => new();
        public Task<Guid> SaveAsync(Item data, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Read_and_count_are_awaited_sequentially_never_concurrently()
    {
        var repo = new ConcurrencyProbeRepository(new List<Item> { new(), new() }, total: 42);
        var wrapper = new AsyncPagedRepositoryWrapper<Item>(repo);

        await wrapper.ReadPagedAsync(page: 1, pageSize: 2);

        repo.MaxConcurrency.Should().Be(1, "the wrapper must not run the read and count on one repository at once");
    }

    [Fact]
    public async Task Assembles_paged_result()
    {
        var repo = new ConcurrencyProbeRepository(new List<Item> { new(), new() }, total: 42);
        var wrapper = new AsyncPagedRepositoryWrapper<Item>(repo);

        var result = await wrapper.ReadPagedAsync(page: 3, pageSize: 2);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(42);
        result.Page.Should().Be(3);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public void Ctor_rejects_null_repository()
    {
        Action act = () => new AsyncPagedRepositoryWrapper<Item>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
