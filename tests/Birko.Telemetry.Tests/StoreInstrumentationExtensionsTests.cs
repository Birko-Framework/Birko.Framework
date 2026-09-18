using Birko.Data.Stores;
using Birko.Telemetry;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Telemetry.Tests;

/// <summary>
/// CR-L379: the fluent wrap helpers (WithInstrumentation / WithBulkInstrumentation /
/// WithAsyncInstrumentation / WithAsyncBulkInstrumentation) — the primary public entry points — had no
/// tests. Each must return the matching wrapper type wrapping the supplied store.
/// </summary>
public class StoreInstrumentationExtensionsTests
{
    public sealed class TestModel : Data.Models.AbstractModel { }

    [Fact]
    public void WithInstrumentation_ReturnsInstrumentedWrapperAroundStore()
    {
        var store = new NoopStore();

        var wrapper = store.WithInstrumentation<NoopStore, TestModel>();

        wrapper.Should().BeOfType<InstrumentedStoreWrapper<NoopStore, TestModel>>();
        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    [Fact]
    public void WithBulkInstrumentation_ReturnsBulkWrapperAroundStore()
    {
        var store = new NoopStore();

        var wrapper = store.WithBulkInstrumentation<NoopStore, TestModel>();

        wrapper.Should().BeOfType<InstrumentedBulkStoreWrapper<NoopStore, TestModel>>();
        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    [Fact]
    public void WithAsyncInstrumentation_ReturnsAsyncWrapperAroundStore()
    {
        var store = new NoopStore();

        var wrapper = store.WithAsyncInstrumentation<NoopStore, TestModel>();

        wrapper.Should().BeOfType<AsyncInstrumentedStoreWrapper<NoopStore, TestModel>>();
        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    [Fact]
    public void WithAsyncBulkInstrumentation_ReturnsAsyncBulkWrapperAroundStore()
    {
        var store = new NoopStore();

        var wrapper = store.WithAsyncBulkInstrumentation<NoopStore, TestModel>();

        wrapper.Should().BeOfType<AsyncInstrumentedBulkStoreWrapper<NoopStore, TestModel>>();
        ((IStoreWrapper)wrapper).GetInnerStore().Should().BeSameAs(store);
    }

    /// <summary>
    /// Implements the full sync + async bulk store surface (IBulkStore&lt;T&gt; : IStore&lt;T&gt; and
    /// IAsyncBulkStore&lt;T&gt; : IAsyncStore&lt;T&gt;), so a single instance satisfies all four helpers. The
    /// wrap helpers only construct a wrapper — they never invoke store methods — so the members throw.
    /// </summary>
    public sealed class NoopStore : IBulkStore<TestModel>, IAsyncBulkStore<TestModel>
    {
        // IStore / IBulkStore (sync)
        public void Init() => throw new NotImplementedException();
        public void Destroy() => throw new NotImplementedException();
        public TestModel CreateInstance() => throw new NotImplementedException();
        public long Count(Expression<Func<TestModel, bool>>? filter = null) => throw new NotImplementedException();
        public TestModel? Read(Guid guid) => throw new NotImplementedException();
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) => throw new NotImplementedException();
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotImplementedException();
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotImplementedException();
        public void Delete(TestModel data) => throw new NotImplementedException();
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotImplementedException();
        public IEnumerable<TestModel> Read() => throw new NotImplementedException();
        public IEnumerable<TestModel> Read(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null) => throw new NotImplementedException();
        public TestModel? ReadFirst(Expression<Func<TestModel, bool>>? filter = null) => throw new NotImplementedException();
        public void Create(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotImplementedException();
        public void Update(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotImplementedException();
        public void Update(Expression<Func<TestModel, bool>> filter, Action<TestModel> updateAction) => throw new NotImplementedException();
        public void Update(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> updates) => throw new NotImplementedException();
        public void Delete(IEnumerable<TestModel> data) => throw new NotImplementedException();
        public void Delete(Expression<Func<TestModel, bool>> filter) => throw new NotImplementedException();

        // IAsyncStore / IAsyncBulkStore (async)
        public Task InitAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task DestroyAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TestModel>> ReadAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TestModel>> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CreateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Expression<Func<TestModel, bool>> filter, Action<TestModel> updateAction, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(IEnumerable<TestModel> data, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Expression<Func<TestModel, bool>> filter, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
