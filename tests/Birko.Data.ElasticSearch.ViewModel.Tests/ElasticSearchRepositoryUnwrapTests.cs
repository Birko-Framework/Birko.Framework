using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.ElasticSearch.Repositories;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.ViewModel.Tests;

/// <summary>
/// CR-M090: Count/ClearCache resolved the concrete store via '(Store as ElasticSearchStore&lt;T&gt;)',
/// which is null whenever Store is a wrapper (e.g. a tenant wrapper) — so Count silently returned 0
/// and ClearCache silently no-op'd. They now go through the unwrapping ElasticSearchStore property.
/// These tests prove the unwrap mechanism the fix relies on distinguishes a wrapped store from the
/// broken direct cast (Count/ClearCache themselves hit a live cluster, so they are not invoked here).
/// </summary>
public class ElasticSearchRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestRepository : ElasticSearchRepository<TestViewModel, TestModel>
    {
        public TestRepository(IStore<TestModel>? store) : base(store) { }

        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal store wrapper (stands in for a tenant wrapper) around an ElasticSearchStore.</summary>
    private sealed class WrappingStore : IStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly ElasticSearchStore<TestModel> _inner;
        public WrappingStore(ElasticSearchStore<TestModel> inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        // IStore<T> surface — the unwrap logic under test only calls GetInnerStore*, never CRUD.
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

    [Fact]
    public void Direct_cast_of_a_wrapped_store_is_null_but_unwrap_finds_the_es_store()
    {
        var es = new ElasticSearchStore<TestModel>();
        var wrapped = new WrappingStore(es);

        // The old, broken path: a direct cast of the wrapper yields null (→ Count 0 / ClearCache no-op).
        (((IStore<TestModel>)wrapped) as ElasticSearchStore<TestModel>).Should().BeNull();

        // The fix's path: GetUnwrappedStore walks the wrapper chain to the real ES store.
        wrapped.GetUnwrappedStore<TestModel, ElasticSearchStore<TestModel>>().Should().BeSameAs(es);
    }

    [Fact]
    public void Repository_ElasticSearchStore_property_unwraps_a_wrapped_store()
    {
        var es = new ElasticSearchStore<TestModel>();
        var repo = new TestRepository(new WrappingStore(es));

        // The property Count/ClearCache now use resolves the wrapped store to the concrete ES store.
        repo.ElasticSearchStore.Should().BeSameAs(es);
    }

    [Fact]
    public void Repository_rejects_a_store_that_is_not_an_es_store_or_wrapper()
    {
        // A wrapper whose inner is not an ElasticSearchStore must fail the ctor guard.
        Action act = () => new TestRepository(new NotAnEsStore());

        act.Should().Throw<ArgumentException>();
    }

    private sealed class NotAnEsStore : IStore<TestModel>
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
}
