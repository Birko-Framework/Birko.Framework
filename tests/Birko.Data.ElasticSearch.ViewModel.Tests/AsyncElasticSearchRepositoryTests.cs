using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Repositories;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.ViewModel.Tests;

/// <summary>
/// CR-L115/L116: the async repository exposed only the store property + ctor, while the sync sibling
/// surfaced Count/ClearCache/Read(SearchRequest). It now has CountAsync/ClearCacheAsync/ReadAsync that
/// delegate through the unwrapping ElasticSearchStore property. Count/ClearCache/Read hit a live cluster,
/// so these tests lock in the ctor validation, the unwrap the new methods rely on, and that the async
/// helper surface exists (mirroring the sync repository).
/// </summary>
public class AsyncElasticSearchRepositoryTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestAsyncRepository : AsyncElasticSearchRepository<TestViewModel, TestModel>
    {
        public TestAsyncRepository(IAsyncStore<TestModel>? store) : base(store) { }

        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal async store wrapper (stands in for a tenant wrapper) around an AsyncElasticSearchStore.</summary>
    private sealed class WrappingAsyncStore : IAsyncStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly AsyncElasticSearchStore<TestModel> _inner;
        public WrappingAsyncStore(AsyncElasticSearchStore<TestModel> inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        // IAsyncStore<T> surface — the unwrap logic under test only calls GetInnerStore*, never CRUD.
        public Task InitAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DestroyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) => throw new NotSupportedException();
        public TestModel CreateInstance() => throw new NotSupportedException();
        public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NotAnEsStore : IAsyncStore<TestModel>
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
    public void Repository_ElasticSearchStore_property_unwraps_a_wrapped_store()
    {
        var es = new AsyncElasticSearchStore<TestModel>();
        var repo = new TestAsyncRepository(new WrappingAsyncStore(es));

        // The property CountAsync/ClearCacheAsync/ReadAsync delegate through resolves the wrapped store.
        repo.ElasticSearchStore.Should().BeSameAs(es);
    }

    [Fact]
    public void Repository_rejects_a_store_that_is_not_an_es_store_or_wrapper()
    {
        Action act = () => new TestAsyncRepository(new NotAnEsStore());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CountAsync_returns_zero_when_no_store_is_resolved()
    {
        // A repo constructed with a null store has no unwrappable ES store, so Count degrades to 0
        // (rather than dereferencing a null store), mirroring the sync repository's Count.
        var repo = new TestAsyncRepository(null);

        // CR-L115: CountAsync(QueryContainer)/ClearCacheAsync/ReadAsync(SearchRequest) exist on the async
        // repo (this call would not compile otherwise), mirroring the sync repository's helper surface.
        (await repo.CountAsync(null)).Should().Be(0);
    }
}
