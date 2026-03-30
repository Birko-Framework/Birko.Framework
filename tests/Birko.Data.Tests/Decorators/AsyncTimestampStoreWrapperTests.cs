using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
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

public class AsyncTimestampStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, ITimestamped
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PrevUpdatedAt { get; set; }
    }

    private class TestAsyncStore : AbstractAsyncStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) =>
            Task.FromResult<TestModel?>(_data.GetValueOrDefault(guid));

        public override Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
        {
            if (filter == null) return Task.FromResult<TestModel?>(_data.Values.FirstOrDefault());
            return Task.FromResult<TestModel?>(_data.Values.AsQueryable().FirstOrDefault(filter));
        }

        public override Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) =>
            Task.FromResult((long)_data.Count);

        public override Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return Task.FromResult(data.Guid.Value);
        }

        public override Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
            return Task.CompletedTask;
        }

        public override Task DeleteAsync(TestModel data, CancellationToken ct = default)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
            return Task.CompletedTask;
        }

        public override Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
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
        var act = () => new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(null!, clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        var store = new TestAsyncStore();
        var act = () => new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_SetsTimestamps()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        model.CreatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(FixedTime);
        model.PrevUpdatedAt.Should().BeNull();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_ShiftsUpdatedAtToPrev()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.CreateAsync(model);

        var laterTime = FixedTime.AddHours(1);
        clock.SetTime(laterTime);
        await wrapper.UpdateAsync(model);

        model.PrevUpdatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(laterTime);
    }

    #endregion

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_NewEntity_SetsTimestamps()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.SaveAsync(model);

        model.CreatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(FixedTime);
    }

    [Fact]
    public async Task SaveAsync_ExistingEntity_ShiftsTimestamps()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        await wrapper.SaveAsync(model);

        var laterTime = FixedTime.AddHours(1);
        clock.SetTime(laterTime);
        await wrapper.SaveAsync(model);

        model.PrevUpdatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(laterTime);
    }

    #endregion

    #region ReadAsync (passthrough)

    [Fact]
    public async Task ReadAsync_DelegatesToInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };
        await wrapper.CreateAsync(model);

        (await wrapper.ReadAsync(model.Guid!.Value)).Should().NotBeNull();
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestAsyncStore();
        var wrapper = new AsyncTimestampStoreWrapper<TestAsyncStore, TestModel>(store, clock);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
