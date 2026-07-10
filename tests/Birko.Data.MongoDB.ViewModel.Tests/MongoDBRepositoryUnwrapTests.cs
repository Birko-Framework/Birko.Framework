using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Repositories;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.ViewModel.Tests;

/// <summary>
/// CR-M121: the MongoDB ViewModel repositories had no test project. These cover the constructor
/// store-type validation (success + ArgumentException) and the unwrapping MongoDBStore getter, using
/// a fake wrapper around an AsyncMongoDBStore — no live MongoDB required.
/// </summary>
public class MongoDBRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestAsyncRepository : AsyncMongoDBRepository<TestViewModel, TestModel>
    {
        public TestAsyncRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    /// <summary>Minimal store wrapper (stands in for a tenant wrapper) around an AsyncMongoDBStore.</summary>
    private sealed class WrappingStore : IAsyncStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly AsyncMongoDBStore<TestModel> _inner;
        public WrappingStore(AsyncMongoDBStore<TestModel> inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        // IAsyncStore<T> surface — never invoked by the unwrap logic under test.
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

    private sealed class NotAMongoStore : IAsyncStore<TestModel>
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
    public void Direct_cast_of_a_wrapped_store_is_null_but_unwrap_finds_the_mongo_store()
    {
        var mongo = new AsyncMongoDBStore<TestModel>();
        var wrapped = new WrappingStore(mongo);

        (((IAsyncStore<TestModel>)wrapped) as AsyncMongoDBStore<TestModel>).Should().BeNull();
        wrapped.GetUnwrappedStore<TestModel, AsyncMongoDBStore<TestModel>>().Should().BeSameAs(mongo);
    }

    [Fact]
    public void Repository_MongoDBStore_property_unwraps_a_wrapped_store()
    {
        var mongo = new AsyncMongoDBStore<TestModel>();
        var repo = new TestAsyncRepository(new WrappingStore(mongo));

        repo.MongoDBStore.Should().BeSameAs(mongo);
    }

    [Fact]
    public void Repository_ctor_accepts_a_plain_mongo_store()
    {
        Action act = () => new TestAsyncRepository(new AsyncMongoDBStore<TestModel>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Repository_ctor_rejects_a_store_that_is_not_a_mongo_store_or_wrapper()
    {
        Action act = () => new TestAsyncRepository(new NotAMongoStore());

        act.Should().Throw<ArgumentException>();
    }
}
