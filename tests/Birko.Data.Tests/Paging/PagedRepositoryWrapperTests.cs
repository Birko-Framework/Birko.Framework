using Birko.Data.Models;
using Birko.Data.Patterns.Paging;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Paging;

public class PagedRepositoryWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class TestBulkStore : AbstractBulkStore<TestModel>
    {
        private readonly List<TestModel> _data = new();

        public void Seed(IEnumerable<TestModel> items) => _data.AddRange(items);

        protected override long CountCore(Expression<Func<TestModel, bool>>? filter = null)
        {
            if (filter == null) return _data.Count;
            return _data.AsQueryable().Count(filter);
        }

        protected override IEnumerable<TestModel> ReadCore(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null)
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
            return query;
        }

        public override TestModel? Read(Guid guid) => _data.FirstOrDefault(x => x.Guid == guid);
        protected override TestModel? ReadCore(Expression<Func<TestModel, bool>>? filter = null) => filter == null ? _data.FirstOrDefault() : _data.AsQueryable().FirstOrDefault(filter);
        protected override Guid CreateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { _data.Add(data); return data.Guid ?? Guid.Empty; }
        protected override void CreateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) => _data.AddRange(data);
        protected override void UpdateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { }
        protected override void UpdateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) { }
        protected override void DeleteCore(TestModel data) => _data.Remove(data);
        protected override void DeleteCore(IEnumerable<TestModel> data) { foreach (var d in data) _data.Remove(d); }
        protected override void InitCore() { }
        public override void Destroy() { }
        public override TestModel CreateInstance() => new();
    }

    private class TestBulkRepository : AbstractBulkRepository<TestModel>
    {
        public TestBulkRepository(IBulkStore<TestModel> store) : base(store) { }
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
    public void ReadPaged_ReturnsCorrectPage()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(50));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 2, pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(50);
        result.TotalPages.Should().Be(5);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void ReadPaged_FirstPage_HasNoPreviousPage()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(25));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 1, pageSize: 10);

        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void ReadPaged_LastPage_HasNoNextPage()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(25));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 3, pageSize: 10);

        result.Items.Should().HaveCount(5);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void ReadPaged_WithFilter_FiltersBeforePaging()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(50));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(
            filter: x => x.Value <= 20,
            page: 1,
            pageSize: 10);

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(20);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void ReadPaged_WithOrderBy_SortsCorrectly()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(30));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(
            orderBy: OrderBy<TestModel>.ByDescending(x => x.Value),
            page: 1,
            pageSize: 5);

        result.Items.First().Value.Should().Be(30);
        result.Items.Last().Value.Should().Be(26);
    }

    [Fact]
    public void ReadPaged_EmptyStore_ReturnsEmptyResult()
    {
        var store = new TestBulkStore();
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 1, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void ReadPaged_PageBeyondData_ReturnsEmptyItems()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(5));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 3, pageSize: 10);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void ReadPaged_InvalidPageClampsToOne()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(10));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: -1, pageSize: 5);

        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(5);
    }

    [Fact]
    public void ReadPaged_InvalidPageSizeClampsToOne()
    {
        var store = new TestBulkStore();
        store.Seed(CreateTestData(10));
        var repo = new TestBulkRepository(store);
        var paged = new PagedRepositoryWrapper<TestModel>(repo);

        var result = paged.ReadPaged(page: 1, pageSize: 0);

        result.PageSize.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public void Constructor_NullRepository_Throws()
    {
        var act = () => new PagedRepositoryWrapper<TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
