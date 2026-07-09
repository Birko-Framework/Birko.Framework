using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Repositories;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.ViewModel.Tests;

/// <summary>
/// CR-M096: the Destroy/DestroyAsync overrides on the InfluxDB repositories called base (which already
/// destroys the store) AND then Drop/DropAsync — destroying the same store an extra time (the InfluxDB
/// bucket dropped again) per Destroy. The overrides were removed; Drop/DropAsync remain as distinct API.
/// Also covers the ctor type-guard and IsHealthy (CR-M097 — the ViewModel repo had no test project).
/// </summary>
public class InfluxDBRepositoryDestroyTests
{
    private class TestModel : AbstractModel { }

    private class TestViewModel : ILoadable<TestModel>
    {
        public void LoadFrom(TestModel data) { }
    }

    private class TestAsyncRepository : AsyncInfluxDBRepository<TestViewModel, TestModel>
    {
        public TestAsyncRepository(IAsyncStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    private class TestSyncRepository : InfluxDBRepository<TestViewModel, TestModel>
    {
        public TestSyncRepository(IStore<TestModel>? store) : base(store) { }
        protected override void MapToModel(TestViewModel source, TestModel target) { }
    }

    [Fact]
    public void AsyncRepository_no_longer_overrides_DestroyAsync()
    {
        // The redundant override is gone → DestroyAsync resolves to a base declaration, not the repo.
        var method = typeof(TestAsyncRepository).GetMethod(
            nameof(TestAsyncRepository.DestroyAsync), new[] { typeof(CancellationToken) })!;

        method.DeclaringType.Should().NotBe(typeof(AsyncInfluxDBRepository<TestViewModel, TestModel>),
            "CR-M096: the DestroyAsync override that double-dropped the bucket was removed");
    }

    [Fact]
    public void SyncRepository_no_longer_overrides_Destroy()
    {
        var method = typeof(TestSyncRepository).GetMethod(nameof(TestSyncRepository.Destroy), System.Type.EmptyTypes)!;

        method.DeclaringType.Should().NotBe(typeof(InfluxDBRepository<TestViewModel, TestModel>),
            "CR-M096: the Destroy override that double-dropped the bucket was removed");
    }

    [Fact]
    public void DropAsync_and_Drop_remain_as_distinct_public_api()
    {
        typeof(AsyncInfluxDBRepository<TestViewModel, TestModel>)
            .GetMethod("DropAsync", new[] { typeof(CancellationToken) })
            .Should().NotBeNull("an explicit bucket-drop entry point is still offered");

        typeof(InfluxDBRepository<TestViewModel, TestModel>)
            .GetMethod("Drop", System.Type.EmptyTypes)
            .Should().NotBeNull();
    }

    [Fact]
    public void AsyncRepository_ctor_rejects_a_wrong_store_type()
    {
        System.Action act = () => new TestAsyncRepository(new WrongAsyncStore());

        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void AsyncRepository_IsHealthy_is_false_without_a_client()
    {
        var repo = new TestAsyncRepository(null); // default store, no settings → no client

        repo.IsHealthy().Should().BeFalse();
    }

    private sealed class WrongAsyncStore : IAsyncStore<TestModel>
    {
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountAsync(System.Linq.Expressions.Expression<System.Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<TestModel?> ReadAsync(System.Guid guid, CancellationToken ct = default) => Task.FromResult<TestModel?>(null);
        public Task<TestModel?> ReadAsync(System.Linq.Expressions.Expression<System.Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult<TestModel?>(null);
        public Task<System.Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.FromResult(System.Guid.Empty);
        public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(TestModel data, CancellationToken ct = default) => Task.CompletedTask;
        public TestModel CreateInstance() => new();
        public Task<System.Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default) => Task.FromResult(System.Guid.Empty);
    }
}
