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
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class TimestampStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractLogModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private class TestStore : AbstractBulkStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        protected override long CountCore(Expression<Func<TestModel, bool>>? filter = null)
        {
            if (filter == null) return _data.Count;
            return _data.Values.AsQueryable().Count(filter);
        }

        public override TestModel? Read(Guid guid) => _data.GetValueOrDefault(guid);
        protected override TestModel? ReadCore(Expression<Func<TestModel, bool>>? filter = null) =>
            filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);

        public override IEnumerable<TestModel> Read() => _data.Values;
        protected override IEnumerable<TestModel> ReadCore(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null)
        {
            IEnumerable<TestModel> result = _data.Values;
            if (filter != null) result = result.AsQueryable().Where(filter);
            return result;
        }

        protected override Guid CreateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        protected override void CreateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            foreach (var item in data) Create(item, storeDelegate);
        }

        protected override void UpdateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        protected override void UpdateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            foreach (var item in data) Update(item, storeDelegate);
        }

        protected override void DeleteCore(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        }

        protected override void DeleteCore(IEnumerable<TestModel> data)
        {
            foreach (var item in data) Delete(item);
        }

        protected override void InitCore() { }
        public override void Destroy() { }
        public override TestModel CreateInstance() => new();
    }

    private static readonly DateTime FixedTime = new(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var act = () => new TimestampStoreWrapper<TestStore, TestModel>(null!, clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        var store = new TestStore();
        var act = () => new TimestampStoreWrapper<TestStore, TestModel>(store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_SetsTimestamps()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        model.CreatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(FixedTime);
        model.PrevUpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_OverwritesExistingTimestamps()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel
        {
            Name = "Test",
            CreatedAt = DateTime.MinValue,
            UpdatedAt = DateTime.MinValue,
            PrevUpdatedAt = DateTime.MinValue
        };

        wrapper.Create(model);

        model.CreatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(FixedTime);
        model.PrevUpdatedAt.Should().BeNull();
    }

    #endregion

    #region Update

    [Fact]
    public void Update_ShiftsUpdatedAtToPrev()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        var laterTime = FixedTime.AddHours(1);
        clock.SetTime(laterTime);
        wrapper.Update(model);

        model.PrevUpdatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(laterTime);
        model.CreatedAt.Should().Be(FixedTime);
    }

    [Fact]
    public void Update_MultipleUpdates_ChainsPrevUpdatedAt()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        var time2 = FixedTime.AddHours(1);
        clock.SetTime(time2);
        wrapper.Update(model);

        var time3 = FixedTime.AddHours(2);
        clock.SetTime(time3);
        wrapper.Update(model);

        model.PrevUpdatedAt.Should().Be(time2);
        model.UpdatedAt.Should().Be(time3);
        model.CreatedAt.Should().Be(FixedTime);
    }

    #endregion

    #region Save

    [Fact]
    public void Save_NewEntity_DelegatesToCreate()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Save(model);

        model.CreatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(FixedTime);
        model.PrevUpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Save_ExistingEntity_DelegatesToUpdate()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        var laterTime = FixedTime.AddHours(1);
        clock.SetTime(laterTime);
        wrapper.Save(model);

        model.PrevUpdatedAt.Should().Be(FixedTime);
        model.UpdatedAt.Should().Be(laterTime);
    }

    #endregion

    #region Delegation

    [Fact]
    public void Read_DelegatesToInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        var read = wrapper.Read(model.Guid!.Value);

        read.Should().NotBeNull();
        read!.Name.Should().Be("Test");
    }

    [Fact]
    public void Delete_DelegatesToInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Delete(model);

        wrapper.Read(model.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public void Count_DelegatesToInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);

        wrapper.Create(new TestModel { Name = "A" });
        wrapper.Create(new TestModel { Name = "B" });

        wrapper.Count().Should().Be(2);
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampStoreWrapper<TestStore, TestModel>(store, clock);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion

    #region Bulk

    [Fact]
    public void Bulk_Create_SetsTimestampsOnAllItems()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampBulkStoreWrapper<TestStore, TestModel>(store, clock);

        var items = new[]
        {
            new TestModel { Name = "A" },
            new TestModel { Name = "B" },
            new TestModel { Name = "C" }
        };

        wrapper.Create(items);

        foreach (var item in items)
        {
            item.CreatedAt.Should().Be(FixedTime);
            item.UpdatedAt.Should().Be(FixedTime);
            item.PrevUpdatedAt.Should().BeNull();
        }
    }

    [Fact]
    public void Bulk_Update_ShiftsTimestampsOnAllItems()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampBulkStoreWrapper<TestStore, TestModel>(store, clock);

        var items = new[]
        {
            new TestModel { Name = "A" },
            new TestModel { Name = "B" }
        };

        wrapper.Create(items);

        var laterTime = FixedTime.AddHours(1);
        clock.SetTime(laterTime);
        wrapper.Update(items);

        foreach (var item in items)
        {
            item.PrevUpdatedAt.Should().Be(FixedTime);
            item.UpdatedAt.Should().Be(laterTime);
            item.CreatedAt.Should().Be(FixedTime);
        }
    }

    [Fact]
    public void Bulk_Read_DelegatesToInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new TimestampBulkStoreWrapper<TestStore, TestModel>(store, clock);

        wrapper.Create(new TestModel { Name = "A" });
        wrapper.Create(new TestModel { Name = "B" });

        wrapper.Read().Should().HaveCount(2);
    }

    #endregion

    #region AbstractLogModel defaults removed

    [Fact]
    public void AbstractLogModel_DefaultTimestamps_AreNotUtcNow()
    {
        var model = new TestModel();

        model.CreatedAt.Should().Be(default(DateTime));
        model.UpdatedAt.Should().Be(default(DateTime));
        model.PrevUpdatedAt.Should().BeNull();
    }

    #endregion
}
