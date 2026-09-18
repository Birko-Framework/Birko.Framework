using Birko.Data.Stores;
using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Telemetry.Tests;

/// <summary>
/// CR-M253: the bulk store wrappers had no tests. CR-M252: they didn't override ReadFirst, so the
/// IBulkStore default ran and bypassed the inner store's native single-row path. These assert the bulk
/// overloads delegate to the inner store, and specifically that ReadFirst reaches the inner ReadFirst.
/// </summary>
public class InstrumentedBulkStoreWrapperTests
{
    public class TestModel : Data.Models.AbstractModel { }

    private sealed class MockBulkStore : IBulkStore<TestModel>
    {
        public string? LastOperation { get; private set; }

        // IStore
        public void Init() => LastOperation = "Init";
        public void Destroy() => LastOperation = "Destroy";
        public TestModel CreateInstance() => new();
        public long Count(Expression<Func<TestModel, bool>>? filter = null) { LastOperation = "Count"; return 0; }
        public TestModel? Read(Guid guid) { LastOperation = "Read(Guid)"; return null; }
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) { LastOperation = "Read(single)"; return null; }
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { LastOperation = "Create(single)"; return Guid.Empty; }
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => LastOperation = "Update(single)";
        public void Delete(TestModel data) => LastOperation = "Delete(single)";
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { LastOperation = "Save"; return Guid.Empty; }

        // Bulk
        public IEnumerable<TestModel> Read() { LastOperation = "Read(all)"; return Array.Empty<TestModel>(); }
        public IEnumerable<TestModel> Read(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null)
        { LastOperation = "Read(bulk)"; return Array.Empty<TestModel>(); }
        public TestModel? ReadFirst(Expression<Func<TestModel, bool>>? filter = null) { LastOperation = "ReadFirst(native)"; return null; }
        public void Create(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) => LastOperation = "Create(bulk)";
        public void Update(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) => LastOperation = "Update(bulk)";
        public void Update(Expression<Func<TestModel, bool>> filter, Action<TestModel> updateAction) => LastOperation = "Update(filter,action)";
        public void Update(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> updates) => LastOperation = "Update(filter,props)";
        public void Delete(IEnumerable<TestModel> data) => LastOperation = "Delete(bulk)";
        public void Delete(Expression<Func<TestModel, bool>> filter) => LastOperation = "Delete(filter)";
    }

    private static (MockBulkStore inner, InstrumentedBulkStoreWrapper<MockBulkStore, TestModel> wrapper) New()
    {
        var inner = new MockBulkStore();
        return (inner, new InstrumentedBulkStoreWrapper<MockBulkStore, TestModel>(inner));
    }

    [Fact]
    public void ReadFirst_DelegatesToInnerReadFirst_NotSingleRead()
    {
        var (inner, wrapper) = New();
        wrapper.ReadFirst(x => true);
        inner.LastOperation.Should().Be("ReadFirst(native)",
            "the wrapper must call the inner store's ReadFirst, not route through the IBulkStore default → Read (CR-M252)");
    }

    [Fact]
    public void BulkRead_DelegatesToInner()
    {
        var (inner, wrapper) = New();
        wrapper.Read(x => true);
        inner.LastOperation.Should().Be("Read(bulk)");
    }

    [Fact]
    public void BulkCreate_UpdateVariants_Delete_DelegateToInner()
    {
        var (inner, wrapper) = New();

        wrapper.Create(new[] { new TestModel() });
        inner.LastOperation.Should().Be("Create(bulk)");

        wrapper.Update(new[] { new TestModel() });
        inner.LastOperation.Should().Be("Update(bulk)");

        wrapper.Update(x => true, m => { });
        inner.LastOperation.Should().Be("Update(filter,action)");

        wrapper.Update(x => true, new PropertyUpdate<TestModel>());
        inner.LastOperation.Should().Be("Update(filter,props)");

        wrapper.Delete(new[] { new TestModel() });
        inner.LastOperation.Should().Be("Delete(bulk)");

        wrapper.Delete(x => true);
        inner.LastOperation.Should().Be("Delete(filter)");
    }
}
