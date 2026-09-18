using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class AsyncSoftDeleteStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, ISoftDeletable
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? DeletedAt { get; set; }
    }

    private class TestAsyncStore : AbstractAsyncStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) =>
            Task.FromResult<TestModel?>(_data.GetValueOrDefault(guid));

        protected override Task<TestModel?> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
            return Task.FromResult<TestModel?>(_data.Values.AsQueryable().FirstOrDefault(filter));
        }

        protected override Task<long> CountCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult((long)_data.Count);
            return Task.FromResult((long)_data.Values.AsQueryable().Count(filter));
        }

        protected override Task<Guid> CreateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        protected override Task UpdateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
            return Task.CompletedTask;
        }

        protected override Task DeleteCoreAsync(TestModel data, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            return Task.CompletedTask;
        }

        protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public override TestModel CreateInstance() => new();
    }

    private static readonly DateTime FixedTime = new(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var act = () => new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(null!, clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        var store = new TestAsyncStore();
        var act = () => new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_ClearsDeletedAt()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test", DeletedAt = DateTime.UtcNow };

        await wrapper.CreateAsync(model);

        model.DeletedAt.Should().BeNull();
    }

    #endregion

    #region DeleteAsync (Soft)

    [Fact]
    public async Task DeleteAsync_SetsDeletedAt_InsteadOfRemoving()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        await wrapper.DeleteAsync(model);

        model.DeletedAt.Should().Be(FixedTime);
        (await store.ReadAsync(model.Guid!.Value)).Should().NotBeNull();
    }

    #endregion

    #region ReadAsync (filters deleted)

    [Fact]
    public async Task ReadAsync_ByGuid_ReturnsNull_ForDeletedEntity()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);
        await wrapper.DeleteAsync(model);

        (await wrapper.ReadAsync(model.Guid!.Value)).Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ByGuid_ReturnsEntity_WhenNotDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        (await wrapper.ReadAsync(model.Guid!.Value)).Should().NotBeNull();
    }

    [Fact]
    public async Task ReadAsync_WithFilter_ExcludesDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);

        var active = new TestModel { Name = "Active" };
        var deleted = new TestModel { Name = "Deleted" };

        await wrapper.CreateAsync(active);
        await wrapper.CreateAsync(deleted);
        await wrapper.DeleteAsync(deleted);

        (await wrapper.ReadAsync(x => x.Name == "Deleted")).Should().BeNull();
    }

    [Fact]
    public async Task CountAsync_ExcludesDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);

        await wrapper.CreateAsync(new TestModel { Name = "A" });
        await wrapper.CreateAsync(new TestModel { Name = "B" });
        var toDelete = new TestModel { Name = "C" };
        await wrapper.CreateAsync(toDelete);
        await wrapper.DeleteAsync(toDelete);

        (await wrapper.CountAsync()).Should().Be(2);
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncSoftDeleteStoreWrapper<TestAsyncStore, TestModel>(store, clock);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
