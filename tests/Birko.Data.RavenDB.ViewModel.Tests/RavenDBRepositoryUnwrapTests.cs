using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.RavenDB.Repositories;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.ViewModel.Tests;

/// <summary>
/// CR-M132: the RavenDB ViewModel repositories had no test project. These cover the constructor
/// store-type validation (accepts a plain AsyncRavenDBStore or a wrapper; a foreign store throws
/// ArgumentException) and the unwrapping RavenDBStore getter via a fake wrapper — no live RavenDB.
/// </summary>
public class RavenDBRepositoryUnwrapTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestAsyncRepository : AsyncRavenDBRepository<TestViewModel, TestModel>
    {
        public TestAsyncRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    private sealed class WrappingStore : IAsyncStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly AsyncRavenDBStore<TestModel> _inner;
        public WrappingStore(AsyncRavenDBStore<TestModel> inner) => _inner = inner;

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

    private sealed class NotARavenStore : IAsyncStore<TestModel>
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
    public void Direct_cast_of_a_wrapped_store_is_null_but_unwrap_finds_the_raven_store()
    {
        var raven = new AsyncRavenDBStore<TestModel>();
        var wrapped = new WrappingStore(raven);

        (((IAsyncStore<TestModel>)wrapped) as AsyncRavenDBStore<TestModel>).Should().BeNull();
        wrapped.GetUnwrappedStore<TestModel, AsyncRavenDBStore<TestModel>>().Should().BeSameAs(raven);
    }

    [Fact]
    public void Repository_RavenDBStore_property_unwraps_a_wrapped_store()
    {
        var raven = new AsyncRavenDBStore<TestModel>();
        var repo = new TestAsyncRepository(new WrappingStore(raven));

        repo.RavenDBStore.Should().BeSameAs(raven);
    }

    [Fact]
    public void Repository_ctor_accepts_a_plain_raven_store()
    {
        Action act = () => new TestAsyncRepository(new AsyncRavenDBStore<TestModel>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Repository_ctor_rejects_a_store_that_is_not_a_raven_store_or_wrapper()
    {
        Action act = () => new TestAsyncRepository(new NotARavenStore());

        act.Should().Throw<ArgumentException>();
    }
}
