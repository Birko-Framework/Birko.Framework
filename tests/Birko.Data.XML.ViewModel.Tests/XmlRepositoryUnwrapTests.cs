using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.XML.Repositories;
using Birko.Data.XML.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.ViewModel.Tests;

/// <summary>
/// CR-L247: Birko.Data.XML.ViewModel had no .Tests sibling. The only behavior the two repositories add
/// over the base is the constructor store-type guard — accept a raw or wrapped XmlStore/AsyncXmlStore,
/// reject a foreign store (ArgumentException), accept null (Store stays unset) — plus the XmlStore
/// unwrap property (resolving the concrete store through a wrapper where a plain cast is null).
/// CR-L246: the guard now validates before the base assigns Store (via a private ValidateStore helper),
/// replacing the misleading base(null) + conditional-assign dance.
/// </summary>
public class XmlRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestXmlRepository : XmlRepository<TestViewModel, TestModel>
    {
        public TestXmlRepository(IStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    private class TestAsyncXmlRepository : AsyncXmlRepository<TestViewModel, TestModel>
    {
        public TestAsyncXmlRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal sync store wrapper (stands in for a tenant wrapper) around an XmlStore.</summary>
    private sealed class WrappingStore : IStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly XmlStore<TestModel> _inner;
        public WrappingStore(XmlStore<TestModel> inner) => _inner = inner;
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

    private sealed class NotAnXmlStore : IStore<TestModel>
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

    private sealed class NotAnAsyncXmlStore : IAsyncStore<TestModel>
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

    // ---- sync ----

    [Fact]
    public void Sync_repo_accepts_a_raw_XmlStore_and_resolves_it()
    {
        var store = new XmlStore<TestModel>();
        var repo = new TestXmlRepository(store);
        repo.XmlStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Sync_repo_resolves_an_XmlStore_through_a_wrapper()
    {
        var store = new XmlStore<TestModel>();
        var repo = new TestXmlRepository(new WrappingStore(store));

        // A plain cast of the wrapper is null; the unwrapping property walks the chain to the real store.
        (((IStore<TestModel>)new WrappingStore(store)) as XmlStore<TestModel>).Should().BeNull();
        repo.XmlStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Sync_repo_rejects_a_foreign_store()
    {
        Action act = () => new TestXmlRepository(new NotAnXmlStore());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sync_repo_accepts_null_and_leaves_store_unset()
    {
        var repo = new TestXmlRepository(null);
        repo.XmlStore.Should().BeNull();
    }

    // ---- async ----

    [Fact]
    public void Async_repo_accepts_a_raw_AsyncXmlStore_and_resolves_it()
    {
        var store = new AsyncXmlStore<TestModel>();
        var repo = new TestAsyncXmlRepository(store);
        repo.XmlStore.Should().BeSameAs(store);
    }

    [Fact]
    public void Async_repo_rejects_a_foreign_store()
    {
        Action act = () => new TestAsyncXmlRepository(new NotAnAsyncXmlStore());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Async_repo_accepts_null_and_leaves_store_unset()
    {
        var repo = new TestAsyncXmlRepository(null);
        repo.XmlStore.Should().BeNull();
    }
}
