using Birko.Data.Models;
using Birko.Data.Patterns.Concurrency;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.Tests.Concurrency;

public class VersionedStoreWrapperTests
{
    #region Test Infrastructure

    private class TestModel : AbstractModel, IVersioned
    {
        public string Name { get; set; } = string.Empty;
        public long Version { get; set; }
    }

    private class TestStore : AbstractStore<TestModel>
    {
        private readonly Dictionary<Guid, TestModel> _data = new();

        public override long Count(Expression<Func<TestModel, bool>>? filter = null)
        {
            if (filter == null) return _data.Count;
            return _data.Values.AsQueryable().Count(filter);
        }

        public override TestModel? Read(Guid guid) => _data.GetValueOrDefault(guid);
        public override TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) =>
            filter == null ? _data.Values.FirstOrDefault() : _data.Values.AsQueryable().FirstOrDefault(filter);

        public override Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            _data[data.Guid.Value] = data;
            return data.Guid.Value;
        }

        public override void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        }

        public override void Delete(TestModel data)
        {
            if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        }

        public override void Init() { }
        public override void Destroy() { }
        public override TestModel CreateInstance() => new();
        public override Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
        {
            if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
            Update(data, storeDelegate);
            return data.Guid.Value;
        }
    }

    #endregion

    [Fact]
    public void Create_SetsVersionToOne()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);

        model.Version.Should().Be(1);
    }

    [Fact]
    public void Update_IncrementsVersion()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        model.Version.Should().Be(1);

        wrapper.Update(model);
        model.Version.Should().Be(2);

        wrapper.Update(model);
        model.Version.Should().Be(3);
    }

    [Fact]
    public void Update_StaleVersion_ThrowsConcurrentUpdateException()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        // model.Version == 1

        // Simulate another process: create a second reference and update it directly
        var otherProcess = new TestModel { Guid = model.Guid, Name = "Updated by other", Version = 1 };
        wrapper.Update(otherProcess);
        // Store now has Version == 2

        // Our model still has Version == 1 (stale)
        model.Version = 1;
        var act = () => wrapper.Update(model);

        act.Should().Throw<ConcurrentUpdateException>()
            .Which.ExpectedVersion.Should().Be(1);
    }

    [Fact]
    public void Save_NewEntity_SetsVersionToOne()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Save(model);

        model.Version.Should().Be(1);
    }

    [Fact]
    public void Save_ExistingEntity_IncrementsVersion()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Save(model);

        model.Version.Should().Be(2);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new VersionedStoreWrapper<TestModel>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConcurrentUpdateException_ContainsEntityInfo()
    {
        var ex = new ConcurrentUpdateException(typeof(TestModel), Guid.NewGuid(), 5);

        ex.EntityType.Should().Be(typeof(TestModel));
        ex.EntityId.Should().NotBeEmpty();
        ex.ExpectedVersion.Should().Be(5);
        ex.Message.Should().Contain("TestModel");
        ex.Message.Should().Contain("5");
    }

    [Fact]
    public void Read_DelegatesToInnerStore()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        var read = wrapper.Read(model.Guid!.Value);

        read.Should().NotBeNull();
        read!.Name.Should().Be("Test");
    }

    [Fact]
    public void Delete_DelegatesToInnerStore()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);
        var model = new TestModel { Name = "Test" };

        wrapper.Create(model);
        wrapper.Delete(model);

        wrapper.Read(model.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public void GetInnerStore_ReturnsInnerStore()
    {
        var store = new TestStore();
        var wrapper = new VersionedStoreWrapper<TestModel>(store);

        wrapper.GetInnerStore().Should().BeSameAs(store);
    }
}
