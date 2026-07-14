using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.JSON.Repositories;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.ViewModel.Tests;

/// <summary>
/// CR-L133: Birko.Data.JSON.ViewModel had no .Tests sibling. These cover the two repositories' public
/// surface that carries behavior: the constructor store-type guard (ArgumentException on a foreign store,
/// acceptance of a raw or wrapped JsonStore) and the JsonStore unwrapping property (resolving the concrete
/// store through a wrapper where a plain cast is null).
/// </summary>
public class JsonRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestJsonRepository : JsonRepository<TestViewModel, TestModel>
    {
        public TestJsonRepository(IStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    private class TestAsyncJsonRepository : AsyncJsonRepository<TestViewModel, TestModel>
    {
        public TestAsyncJsonRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal sync store wrapper (stands in for a tenant wrapper) around a JsonStore.</summary>
    private sealed class WrappingStore : IStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly JsonStore<TestModel> _inner;
        public WrappingStore(JsonStore<TestModel> inner) => _inner = inner;
        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        public void Init() => throw new NotSupportedException();
        public void Destroy() => throw new NotSupportedException();
        public long Count(Expression<Func<TestModel, bool>>? filter = null) => throw new NotSupportedException();
        public TestModel? Read(Guid guid) => throw new NotSupportedException();
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) => throw new NotSupportedException();
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotSupportedException();
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotSupportedException();
        public void Delete(TestModel data) => throw new NotSupportedException();
        public TestModel CreateInstance() => throw new NotSupportedException();
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => throw new NotSupportedException();
    }

    private sealed class NotAJsonStore : IStore<TestModel>
    {
        public void Init() { }
        public void Destroy() { }
        public long Count(Expression<Func<TestModel, bool>>? filter = null) => 0;
        public TestModel? Read(Guid guid) => null;
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) => null;
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => Guid.Empty;
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) { }
        public void Delete(TestModel data) { }
        public TestModel CreateInstance() => new();
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => Guid.Empty;
    }

    private sealed class NotAnAsyncJsonStore : IAsyncStore<TestModel>
    {
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => Task.FromResult<TestModel?>(null);
        public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult<TestModel?>(null);
        public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) => Task.CompletedTask;
        public TestModel CreateInstance() => new();
        public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
    }

    [Fact]
    public void Sync_repo_accepts_a_raw_JsonStore_and_resolves_it()
    {
        var store = new JsonStore<TestModel>();
        var repo = new TestJsonRepository(store);
        repo.JsonStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Sync_repo_resolves_a_JsonStore_through_a_wrapper()
    {
        var store = new JsonStore<TestModel>();
        var repo = new TestJsonRepository(new WrappingStore(store));

        // A plain cast of the wrapper is null; the unwrapping property walks the chain to the real store.
        (((IStore<TestModel>)new WrappingStore(store)) as JsonStore<TestModel>).Should().BeNull();
        repo.JsonStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Sync_repo_rejects_a_foreign_store()
    {
        Action act = () => new TestJsonRepository(new NotAJsonStore());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Async_repo_accepts_a_raw_AsyncJsonStore_and_resolves_it()
    {
        var store = new AsyncJsonStore<TestModel>();
        var repo = new TestAsyncJsonRepository(store);
        repo.JsonStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Async_repo_rejects_a_foreign_store()
    {
        Action act = () => new TestAsyncJsonRepository(new NotAnAsyncJsonStore());
        act.Should().Throw<ArgumentException>();
    }
}
