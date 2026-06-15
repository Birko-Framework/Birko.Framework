using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Tests.Stores
{
    /// <summary>
    /// Tests for <see cref="IBulkReadStore{T}.ReadFirst"/> / <see cref="IAsyncBulkReadStore{T}.ReadFirstAsync"/>.
    /// On a bulk store the inherited single-result <c>Read(filter)</c> is hidden by the bulk overload
    /// (C# member lookup only considers the most-derived type that declares a method of that name), so
    /// <c>store.Read(filter)</c> returns the whole result set. <c>ReadFirst</c> re-exposes the single-result path.
    /// </summary>
    public class BulkStoreReadFirstTests
    {
        private class TestModel : AbstractModel
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        private class TestBulkStore : AbstractBulkStore<TestModel>
        {
            private readonly Dictionary<Guid, TestModel> _data = new();

            protected override void InitCore() { }
            public override void Destroy() => _data.Clear();

            protected override Guid CreateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
            {
                data.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(data);
                _data[data.Guid.Value] = data;
                return data.Guid.Value;
            }

            protected override TestModel? ReadCore(Expression<Func<TestModel, bool>>? filter = null)
                => Filtered(filter).FirstOrDefault();

            protected override IEnumerable<TestModel> ReadCore(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null)
                => Filtered(filter).ToList();

            protected override void UpdateCore(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null)
            {
                if (data.Guid != null && _data.ContainsKey(data.Guid.Value))
                {
                    storeDelegate?.Invoke(data);
                    _data[data.Guid.Value] = data;
                }
            }

            protected override void CreateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
            {
                foreach (var item in data) CreateCore(item, storeDelegate);
            }

            protected override void UpdateCore(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null)
            {
                foreach (var item in data) UpdateCore(item, storeDelegate);
            }

            protected override void DeleteCore(TestModel data)
            {
                if (data.Guid != null) _data.Remove(data.Guid.Value);
            }

            protected override void DeleteCore(IEnumerable<TestModel> data)
            {
                foreach (var item in data) DeleteCore(item);
            }

            protected override long CountCore(Expression<Func<TestModel, bool>>? filter = null)
                => Filtered(filter).Count();

            private IEnumerable<TestModel> Filtered(Expression<Func<TestModel, bool>>? filter)
                => filter == null ? _data.Values : _data.Values.Where(filter.Compile());
        }

        private class TestAsyncBulkStore : AbstractAsyncBulkStore<TestModel>
        {
            private readonly Dictionary<Guid, TestModel> _data = new();

            protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
            public override Task DestroyAsync(CancellationToken ct = default) { _data.Clear(); return Task.CompletedTask; }

            protected override Task<Guid> CreateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
            {
                data.Guid ??= Guid.NewGuid();
                processDelegate?.Invoke(data);
                _data[data.Guid.Value] = data;
                return Task.FromResult(data.Guid.Value);
            }

            protected override Task<TestModel?> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
                => Task.FromResult<TestModel?>(Filtered(filter).FirstOrDefault());

            protected override Task<IEnumerable<TestModel>> ReadCoreAsync(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
                => Task.FromResult<IEnumerable<TestModel>>(Filtered(filter).ToList());

            protected override Task UpdateCoreAsync(TestModel data, StoreDataDelegate<TestModel>? processDelegate = null, CancellationToken ct = default)
            {
                if (data.Guid != null && _data.ContainsKey(data.Guid.Value))
                {
                    processDelegate?.Invoke(data);
                    _data[data.Guid.Value] = data;
                }
                return Task.CompletedTask;
            }

            protected override Task CreateCoreAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default)
            {
                foreach (var item in data) CreateCoreAsync(item, storeDelegate, ct);
                return Task.CompletedTask;
            }

            protected override Task UpdateCoreAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? storeDelegate = null, CancellationToken ct = default)
            {
                foreach (var item in data) UpdateCoreAsync(item, storeDelegate, ct);
                return Task.CompletedTask;
            }

            protected override Task DeleteCoreAsync(TestModel data, CancellationToken ct = default)
            {
                if (data.Guid != null) _data.Remove(data.Guid.Value);
                return Task.CompletedTask;
            }

            protected override Task DeleteCoreAsync(IEnumerable<TestModel> data, CancellationToken ct = default)
            {
                foreach (var item in data) DeleteCoreAsync(item, ct);
                return Task.CompletedTask;
            }

            protected override Task<long> CountCoreAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default)
                => Task.FromResult<long>(Filtered(filter).Count());

            private IEnumerable<TestModel> Filtered(Expression<Func<TestModel, bool>>? filter)
                => filter == null ? _data.Values : _data.Values.Where(filter.Compile());
        }

        /// <summary>
        /// Minimal stand-in for the framework's bulk store wrappers (Tenant, SoftDelete, Audit, Timestamp,
        /// Localized, Instrumented, Validating, EventSourcing, Default…): implements <see cref="IBulkStore{T}"/>
        /// directly and deliberately does NOT override <c>ReadFirst</c>, so it exercises the default interface method.
        /// </summary>
        private class PassthroughBulkWrapper : IBulkStore<TestModel>
        {
            private readonly IBulkStore<TestModel> _inner;
            public PassthroughBulkWrapper(IBulkStore<TestModel> inner) => _inner = inner;

            // Single-result Read: cast the inner to IReadStore<T> so it isn't shadowed by the bulk overload (the real wrappers do exactly this).
            public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) => ((IReadStore<TestModel>)_inner).Read(filter);
            public TestModel? Read(Guid guid) => ((IReadStore<TestModel>)_inner).Read(guid);
            public IEnumerable<TestModel> Read() => _inner.Read();
            public IEnumerable<TestModel> Read(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null) => _inner.Read(filter, orderBy, limit, offset);
            public long Count(Expression<Func<TestModel, bool>>? filter = null) => _inner.Count(filter);
            public Guid Create(TestModel data, StoreDataDelegate<TestModel>? d = null) => _inner.Create(data, d);
            public void Create(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? d = null) => _inner.Create(data, d);
            public void Update(TestModel data, StoreDataDelegate<TestModel>? d = null) => _inner.Update(data, d);
            public void Update(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? d = null) => _inner.Update(data, d);
            public void Update(Expression<Func<TestModel, bool>> filter, Action<TestModel> a) => _inner.Update(filter, a);
            public void Update(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> u) => _inner.Update(filter, u);
            public void Delete(TestModel data) => _inner.Delete(data);
            public void Delete(IEnumerable<TestModel> data) => _inner.Delete(data);
            public void Delete(Expression<Func<TestModel, bool>> filter) => _inner.Delete(filter);
            public Guid Save(TestModel data, StoreDataDelegate<TestModel>? d = null) => _inner.Save(data, d);
            public TestModel CreateInstance() => _inner.CreateInstance();
            public void Init() => _inner.Init();
            public void Destroy() => _inner.Destroy();
        }

        private class PassthroughAsyncBulkWrapper : IAsyncBulkStore<TestModel>
        {
            private readonly IAsyncBulkStore<TestModel> _inner;
            public PassthroughAsyncBulkWrapper(IAsyncBulkStore<TestModel> inner) => _inner = inner;

            public Task<TestModel?> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<TestModel>)_inner).ReadAsync(filter, ct);
            public Task<TestModel?> ReadAsync(Guid guid, CancellationToken ct = default) => ((IAsyncReadStore<TestModel>)_inner).ReadAsync(guid, ct);
            public Task<IEnumerable<TestModel>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
            public Task<IEnumerable<TestModel>> ReadAsync(Expression<Func<TestModel, bool>>? filter = null, OrderBy<TestModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default) => _inner.ReadAsync(filter, orderBy, limit, offset, ct);
            public Task<long> CountAsync(Expression<Func<TestModel, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
            public Task<Guid> CreateAsync(TestModel data, StoreDataDelegate<TestModel>? d = null, CancellationToken ct = default) => _inner.CreateAsync(data, d, ct);
            public Task CreateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? d = null, CancellationToken ct = default) => _inner.CreateAsync(data, d, ct);
            public Task UpdateAsync(TestModel data, StoreDataDelegate<TestModel>? d = null, CancellationToken ct = default) => _inner.UpdateAsync(data, d, ct);
            public Task UpdateAsync(IEnumerable<TestModel> data, StoreDataDelegate<TestModel>? d = null, CancellationToken ct = default) => _inner.UpdateAsync(data, d, ct);
            public Task UpdateAsync(Expression<Func<TestModel, bool>> filter, Action<TestModel> a, CancellationToken ct = default) => _inner.UpdateAsync(filter, a, ct);
            public Task UpdateAsync(Expression<Func<TestModel, bool>> filter, PropertyUpdate<TestModel> u, CancellationToken ct = default) => _inner.UpdateAsync(filter, u, ct);
            public Task DeleteAsync(TestModel data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
            public Task DeleteAsync(IEnumerable<TestModel> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
            public Task DeleteAsync(Expression<Func<TestModel, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(filter, ct);
            public Task<Guid> SaveAsync(TestModel data, StoreDataDelegate<TestModel>? d = null, CancellationToken ct = default) => _inner.SaveAsync(data, d, ct);
            public TestModel CreateInstance() => _inner.CreateInstance();
            public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
            public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        }

        [Fact]
        public void ReadFirst_ShouldReturnSingleMatchingEntity()
        {
            var store = new TestBulkStore();
            store.Create(new TestModel { Name = "a", Value = 1 });
            store.Create(new TestModel { Name = "b", Value = 2 });

            var result = store.ReadFirst(m => m.Name == "b");

            result.Should().NotBeNull();
            result!.Value.Should().Be(2);
        }

        [Fact]
        public void ReadFirst_NoMatch_ShouldReturnNull()
        {
            var store = new TestBulkStore();
            store.Create(new TestModel { Name = "a" });

            store.ReadFirst(m => m.Name == "missing").Should().BeNull();
        }

        [Fact]
        public void Read_WithFilter_ReturnsCollection_WhileReadFirst_ReturnsSingle()
        {
            var store = new TestBulkStore();
            store.Create(new TestModel { Name = "x", Value = 1 });
            store.Create(new TestModel { Name = "x", Value = 2 });

            // The bulk overload wins on the concrete type — this is the shadowing being guarded against.
            IEnumerable<TestModel> many = store.Read(m => m.Name == "x");
            many.Should().HaveCount(2);

            // ReadFirst gives the single-result semantics callers expect from IStore<T>.Read.
            TestModel? one = store.ReadFirst(m => m.Name == "x");
            one.Should().NotBeNull();
        }

        [Fact]
        public void ReadFirst_ShouldBeReachableThroughBulkStoreInterface()
        {
            IBulkStore<TestModel> store = new TestBulkStore();
            store.Create(new TestModel { Name = "iface", Value = 7 });

            store.ReadFirst(m => m.Name == "iface")!.Value.Should().Be(7);
        }

        [Fact]
        public void ReadFirst_DefaultInterfaceMethod_RoutesThroughSingleRead_OnDirectImplementer()
        {
            // Mirrors every store wrapper/decorator: implements IBulkStore<T> directly (no concrete ReadFirst
            // override), so it relies on the IBulkReadStore<T> default interface method, which must route
            // through the wrapper's own single-result Read(filter).
            var inner = new TestBulkStore();
            IBulkStore<TestModel> wrapper = new PassthroughBulkWrapper(inner);
            wrapper.Create(new TestModel { Name = "wrapped", Value = 9 });
            wrapper.Create(new TestModel { Name = "wrapped", Value = 10 });

            wrapper.Read(m => m.Name == "wrapped").Should().HaveCount(2);     // bulk overload still returns the set
            wrapper.ReadFirst(m => m.Value == 10)!.Value.Should().Be(10);     // DIM → single Read
        }

        [Fact]
        public async Task ReadFirstAsync_DefaultInterfaceMethod_RoutesThroughSingleRead_OnDirectImplementer()
        {
            var inner = new TestAsyncBulkStore();
            IAsyncBulkStore<TestModel> wrapper = new PassthroughAsyncBulkWrapper(inner);
            await wrapper.CreateAsync(new TestModel { Name = "wrapped", Value = 9 });
            await wrapper.CreateAsync(new TestModel { Name = "wrapped", Value = 10 });

            (await wrapper.ReadAsync(m => m.Name == "wrapped")).Should().HaveCount(2);
            (await wrapper.ReadFirstAsync(m => m.Value == 10))!.Value.Should().Be(10);
        }

        [Fact]
        public async Task ReadFirstAsync_ShouldReturnSingleMatchingEntity()
        {
            var store = new TestAsyncBulkStore();
            await store.CreateAsync(new TestModel { Name = "a", Value = 1 });
            await store.CreateAsync(new TestModel { Name = "b", Value = 2 });

            var result = await store.ReadFirstAsync(m => m.Name == "b");

            result.Should().NotBeNull();
            result!.Value.Should().Be(2);
        }

        [Fact]
        public async Task ReadFirstAsync_NoMatch_ShouldReturnNull()
        {
            var store = new TestAsyncBulkStore();
            await store.CreateAsync(new TestModel { Name = "a" });

            (await store.ReadFirstAsync(m => m.Name == "missing")).Should().BeNull();
        }

        [Fact]
        public async Task ReadFirstAsync_ShouldBeReachableThroughBulkStoreInterface()
        {
            IAsyncBulkStore<TestModel> store = new TestAsyncBulkStore();
            await store.CreateAsync(new TestModel { Name = "iface", Value = 7 });

            (await store.ReadFirstAsync(m => m.Name == "iface"))!.Value.Should().Be(7);
        }
    }
}
