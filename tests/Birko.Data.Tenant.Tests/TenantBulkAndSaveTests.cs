using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// CR-M173: the tenant bulk wrappers validated the source with All(BelongsToCurrentTenant) and then
/// passed the same lazy IEnumerable to the inner store — enumerating it twice, so a non-deterministic
/// sequence could persist a set different from the one authorized. Materialize once.
/// CR-M174: AsyncTenantStoreWrapper.SaveAsync's create branch ignored the CreateAsync return and relied
/// on the inner store mutating data.Guid in place; it now returns the CreateAsync result (like the sync wrapper).
/// </summary>
public class TenantBulkAndSaveTests
{
    public class Doc : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
    }

    private sealed class CountingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _inner;
        public int EnumerationCount { get; private set; }
        public CountingEnumerable(IEnumerable<T> inner) => _inner = inner;
        public IEnumerator<T> GetEnumerator() { EnumerationCount++; return _inner.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static (AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc> wrapper, Guid tenant) NewBulk()
    {
        var inner = new AsyncInMemoryStore<Doc>();
        var ctx = new TenantContext();
        var tenant = Guid.NewGuid();
        ctx.SetTenant(tenant, "Acme");
        return (new AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<Doc>, Doc>(inner, ctx), tenant);
    }

    [Fact]
    public async Task DeleteAsync_enumerates_the_source_once()
    {
        var (wrapper, tenant) = NewBulk();
        var source = new CountingEnumerable<Doc>(new[] { new Doc { Guid = Guid.NewGuid(), TenantGuid = tenant } });

        await wrapper.DeleteAsync(source);

        source.EnumerationCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_enumerates_the_source_once()
    {
        var (wrapper, tenant) = NewBulk();
        var source = new CountingEnumerable<Doc>(new[] { new Doc { Guid = Guid.NewGuid(), TenantGuid = tenant } });

        await wrapper.UpdateAsync(source);

        source.EnumerationCount.Should().Be(1);
    }

    /// <summary>Inner store that returns a fresh id from CreateAsync WITHOUT writing it back to data.Guid.</summary>
    private sealed class IdAllocatingStore : IAsyncStore<Doc>
    {
        public readonly Guid Allocated = Guid.NewGuid();
        public Task<Guid> CreateAsync(Doc data, StoreDataDelegate<Doc>? storeDelegate = null, CancellationToken ct = default)
            => Task.FromResult(Allocated); // note: does NOT set data.Guid
        public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountAsync(Expression<Func<Doc, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<Doc?> ReadAsync(Guid guid, CancellationToken ct = default) => Task.FromResult<Doc?>(null);
        public Task<Doc?> ReadAsync(Expression<Func<Doc, bool>>? filter = null, CancellationToken ct = default) => Task.FromResult<Doc?>(null);
        public Task UpdateAsync(Doc data, StoreDataDelegate<Doc>? storeDelegate = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Doc data, CancellationToken ct = default) => Task.CompletedTask;
        public Doc CreateInstance() => new();
        public Task<Guid> SaveAsync(Doc data, StoreDataDelegate<Doc>? storeDelegate = null, CancellationToken ct = default) => Task.FromResult(Allocated);
    }

    [Fact]
    public async Task SaveAsync_new_entity_returns_the_inner_store_allocated_id()
    {
        var inner = new IdAllocatingStore();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), "Acme");
        var wrapper = new AsyncTenantStoreWrapper<IdAllocatingStore, Doc>(inner, ctx);

        var result = await wrapper.SaveAsync(new Doc { Name = "x" }); // no Guid → create branch

        result.Should().Be(inner.Allocated, "CR-M174: Save must return the CreateAsync result, not rely on data.Guid write-back");
    }
}
