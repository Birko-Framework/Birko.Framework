using Birko.Data.Models;
using Birko.Data.Patterns.Paging;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Paging;

public class AsyncPagedRepositoryWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class TestAsyncBulkStore : AbstractAsyncBulkStore<TestModel>
    {
        private readonly List<TestModel> _data = new();

        public void Seed(IEnumerable<TestModel> items) => _data.AddRange(items);

        public override Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            long count = filter == null ? _data.Count : _data.AsQueryable().Count(filter);
            return Task.FromResult(count);
        }

        public override Task<IEnumerable<TestModel>> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            IEnumerable<TestModel> query = _data;
            if (filter != null) query = query.AsQueryable().Where(filter);
            if (orderBy != null)
            {
                foreach (var field in orderBy.Fields)
                {
                    var prop = typeof(TestModel).GetProperty(field.PropertyName)!;
                    query = field.Descending
                        ? query.OrderByDescending(x => prop.GetValue(x))
                        : query.OrderBy(x => prop.GetValue(x));
                }
            }
            if (offset.HasValue) query = query.Skip(offset.Value);
            if (limit.HasValue) query = query.Take(limit.Value);
            return Task.FromResult(query);
        }

        public override Task<IEnumerable<TestModel>> ReadAsync(CancellationToken ct = default) => ReadAsync(null, null, null, null, ct);
        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => Task.FromResult(_data.FirstOrDefault(x => x.Guid == guid));
        public override Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult(filter == null ? _data.FirstOrDefault() : _data.AsQueryable().FirstOrDefault(filter));
        public override Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) { _data.Add(data); return Task.FromResult(data.Guid ?? Guid.Empty); }
        public override Task CreateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) { _data.AddRange(data); return Task.CompletedTask; }
        public override Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
        public override Task UpdateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
        public override Task DeleteAsync(TestModel data, CancellationToken ct = default) { _data.Remove(data); return Task.CompletedTask; }
        public override Task DeleteAsync(IEnumerable<TestModel> data, CancellationToken ct = default) { foreach (var d in data) _data.Remove(d); return Task.CompletedTask; }
        public override Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override TestModel CreateInstance() => new();
        public override Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) => CreateAsync(data, processDelegate, ct);
    }

    private class TestAsyncBulkRepository : AbstractAsyncBulkRepository<TestModel>
    {
        public TestAsyncBulkRepository(IAsyncBulkStore<TestModel> store) : base(store) { }
    }

    private static List<TestModel> CreateTestData(int count)
    {
        return Enumerable.Range(1, count).Select(i => new TestModel
        {
            Guid = Guid.NewGuid(),
            Name = $"Item{i}",
            Value = i
        }).ToList();
    }

    #endregion

    [Fact]
    public async Task ReadPagedAsync_ReturnsCorrectPage()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(50));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: 2, pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(50);
        result.TotalPages.Should().Be(5);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task ReadPagedAsync_FirstPage_HasNoPreviousPage()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(25));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: 1, pageSize: 10);

        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task ReadPagedAsync_LastPage_HasNoNextPage()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(25));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: 3, pageSize: 10);

        result.Items.Should().HaveCount(5);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task ReadPagedAsync_WithFilter_FiltersBeforePaging()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(50));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(
            filter: x => x.Value <= 20,
            page: 1,
            pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(20);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ReadPagedAsync_WithOrderBy_SortsCorrectly()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(30));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(
            orderBy: OrderBy<TestModel>.ByDescending(x => x.Value),
            page: 1,
            pageSize: 5);

        result.Items.First().Value.Should().Be(30);
        result.Items.Last().Value.Should().Be(26);
    }

    [Fact]
    public async Task ReadPagedAsync_EmptyStore_ReturnsEmptyResult()
    {
        var store = new TestAsyncBulkStore();
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: 1, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task ReadPagedAsync_PageBeyondData_ReturnsEmptyItems()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(5));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: 3, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task ReadPagedAsync_InvalidPageClampsToOne()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(10));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        var result = await paged.ReadPagedAsync(page: -1, pageSize: 5);

        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task ReadPagedAsync_RunsConcurrently()
    {
        var store = new TestAsyncBulkStore();
        store.Seed(CreateTestData(10));
        var repo = new TestAsyncBulkRepository(store);
        var paged = new AsyncPagedRepositoryWrapper<TestModel>(repo);

        // Verify that multiple concurrent paged reads don't interfere
        var tasks = Enumerable.Range(1, 5).Select(p =>
            paged.ReadPagedAsync(page: 1, pageSize: 2));

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
        {
            r.Items.Should().HaveCount(2);
            r.TotalCount.Should().Be(10);
        });
    }

    [Fact]
    public void Constructor_NullRepository_Throws()
    {
        var act = () => new AsyncPagedRepositoryWrapper<TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
