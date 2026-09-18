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
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class SoftDeleteStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, ISoftDeletable
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? DeletedAt { get; set; }
    }

    private class TestStore : AbstractStore<TestModel>
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

        protected override Guid CreateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        protected override void UpdateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        protected override void DeleteCore(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
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
        var act = () => new SoftDeleteStoreWrapper<TestStore, TestModel>(null!, clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("innerStore");
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        var store = new TestStore();
        var act = () => new SoftDeleteStoreWrapper<TestStore, TestModel>(store, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    #endregion

    #region Create

    [Fact]
    public void Create_ClearsDeletedAt()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test", DeletedAt = DateTime.UtcNow };

        wrapper.Create(model);

        model.DeletedAt.Should().BeNull();
    }

    #endregion

    #region Delete (Soft)

    [Fact]
    public void Delete_SetsDeletedAt_InsteadOfRemoving()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Delete(model);

        model.DeletedAt.Should().Be(FixedTime);
        // Entity still exists in inner store (soft-deleted, not removed)
        store.Read(model.Guid!.Value).Should().NotBeNull();
    }

    #endregion

    #region Read (filters deleted)

    [Fact]
    public void Read_ByGuid_ReturnsNull_ForDeletedEntity()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Delete(model);

        wrapper.Read(model.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public void Read_ByGuid_ReturnsEntity_WhenNotDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        wrapper.Read(model.Guid!.Value).Should().NotBeNull();
    }

    [Fact]
    public void Read_WithFilter_ExcludesDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);

        var active = new TestModel { Name = "Active" };
        var deleted = new TestModel { Name = "Deleted" };

        wrapper.Create(active);
        wrapper.Create(deleted);
        wrapper.Delete(deleted);

        var result = wrapper.Read(x => x.Name == "Deleted");
        result.Should().BeNull();
    }

    [Fact]
    public void Count_ExcludesDeleted()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);

        wrapper.Create(new TestModel { Name = "A" });
        wrapper.Create(new TestModel { Name = "B" });
        var toDelete = new TestModel { Name = "C" };
        wrapper.Create(toDelete);
        wrapper.Delete(toDelete);

        wrapper.Count().Should().Be(2);
    }

    #endregion

    #region Save

    [Fact]
    public void Save_NewEntity_ClearsDeletedAt()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);
        var model = new TestModel { Name = "Test", DeletedAt = DateTime.UtcNow };

        wrapper.Save(model);

        model.DeletedAt.Should().BeNull();
    }

    #endregion

    #region GetInnerStore

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var clock = new TestDateTimeProvider(FixedTime);
        var store = new TestStore();
        var wrapper = new SoftDeleteStoreWrapper<TestStore, TestModel>(store, clock);

        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    #endregion
}
