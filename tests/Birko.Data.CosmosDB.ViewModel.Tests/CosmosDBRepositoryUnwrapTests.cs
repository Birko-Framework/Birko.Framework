using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Repositories;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.ViewModel.Tests;

/// <summary>
/// CR-L108: the CosmosDB ViewModel repositories had no test project. These cover the constructor
/// store-type validation (success + ArgumentException) and the unwrapping CosmosStore getter, using a
/// fake wrapper around an AsyncCosmosDBStore — no live Cosmos / emulator required.
/// </summary>
public class CosmosDBRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestAsyncRepository : AsyncCosmosDBRepository<TestViewModel, TestModel>
    {
        public TestAsyncRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal store wrapper (stands in for a tenant wrapper) around an AsyncCosmosDBStore.</summary>
    private sealed class WrappingStore : IAsyncStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly AsyncCosmosDBStore<TestModel> _inner;
        public WrappingStore(AsyncCosmosDBStore<TestModel> inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        public Task InitAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DestroyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) => throw new NotSupportedException();
        public TestModel CreateInstance() => new();
        public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NotACosmosStore : IAsyncStore<TestModel>
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
    public void Direct_cast_of_a_wrapped_store_is_null_but_unwrap_finds_the_cosmos_store()
    {
        var cosmos = new AsyncCosmosDBStore<TestModel>();
        var wrapped = new WrappingStore(cosmos);

        (((IAsyncStore<TestModel>)wrapped) as AsyncCosmosDBStore<TestModel>).Should().BeNull();
        wrapped.GetUnwrappedStore<TestModel, AsyncCosmosDBStore<TestModel>>().Should().BeSameAs(cosmos);
    }

    [Fact]
    public void Repository_CosmosStore_property_unwraps_a_wrapped_store()
    {
        var cosmos = new AsyncCosmosDBStore<TestModel>();
        var repo = new TestAsyncRepository(new WrappingStore(cosmos));

        repo.CosmosStore.Should().BeSameAs(cosmos);
    }

    [Fact]
    public void Repository_ctor_accepts_a_plain_cosmos_store()
    {
        Action act = () => new TestAsyncRepository(new AsyncCosmosDBStore<TestModel>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Repository_ctor_rejects_a_store_that_is_not_a_cosmos_store_or_wrapper()
    {
        Action act = () => new TestAsyncRepository(new NotACosmosStore());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsHealthy_WithNoStore_ReturnsFalse()
    {
        new TestAsyncRepository(null).IsHealthy().Should().BeFalse();
    }
}
