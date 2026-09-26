using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// TASK-498 — the tenant bulk wrappers carry a <see cref="PropertyUpdate{T}"/> increment through to the inner store
/// with only the filter rewritten: the current tenant's matching row is incremented (added to, never assigned the
/// delta), and another tenant's row that matches the same caller filter is left untouched. Proved on real SQLite
/// (native <c>col = col + @p</c> under the wrapper's combined tenant predicate) and on InMemory (the
/// <c>ApplyTo</c> read-modify-save path).
/// </summary>
public class PropertyUpdateIncrementTenantTests : IDisposable
{
    private readonly string _root;

    public PropertyUpdateIncrementTenantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-tenant-increment-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("TenantCounters")]
    public class TenantCounter : AbstractModel, ITenant
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public string? Name { get; set; }
        public int Hits { get; set; }
    }

    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();

    private static TenantContext CtxFor(Guid tenant, string name)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant, name);
        return ctx;
    }

    private static TenantCounter Row(Guid tenant, string tenantName)
        => new() { Guid = Guid.NewGuid(), TenantGuid = tenant, TenantName = tenantName, Name = "shared", Hits = 10 };

    private void CreateTable(string dbName)
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = dbName });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(TenantCounter) });
    }

    private async Task<AsyncSQLiteStore<TenantCounter>> SeedAsyncStore(string dbName)
    {
        CreateTable(dbName);
        var store = new AsyncSQLiteStore<TenantCounter>();
        store.SetSettings(new SqLiteSettings(_root, dbName));
        await store.CreateAsync(Row(Ours, "Ours"));
        await store.CreateAsync(Row(Theirs, "Theirs"));
        return store;
    }

    private static async Task<int> HitsOfAsync(AsyncSQLiteStore<TenantCounter> store, Guid tenant)
        => (await store.ReadAsync(x => x.TenantGuid == tenant)).Single().Hits;

    [Fact]
    public async Task Async_Increment_Applies_To_The_Current_Tenant_Only()
    {
        var store = await SeedAsyncStore("async.db");
        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantCounter>, TenantCounter>(store, CtxFor(Ours, "Ours"));

        await wrapper.UpdateAsync(x => x.Name == "shared", new PropertyUpdate<TenantCounter>().Increment(x => x.Hits, 5));

        (await HitsOfAsync(store, Ours)).Should().Be(15, "+5 on 10 — an assigned delta would read 5");
        (await HitsOfAsync(store, Theirs)).Should().Be(10, "the other tenant's row matches the caller filter but not the tenant scope");
    }

    [Fact]
    public async Task Async_Decrement_Applies_To_The_Current_Tenant_Only()
    {
        var store = await SeedAsyncStore("async-dec.db");
        var wrapper = new AsyncTenantBulkStoreWrapper<AsyncSQLiteStore<TenantCounter>, TenantCounter>(store, CtxFor(Theirs, "Theirs"));

        await wrapper.UpdateAsync(x => x.Name == "shared", new PropertyUpdate<TenantCounter>().Decrement(x => x.Hits, 3));

        (await HitsOfAsync(store, Theirs)).Should().Be(7);
        (await HitsOfAsync(store, Ours)).Should().Be(10);
    }

    [Fact]
    public void Sync_Increment_Applies_To_The_Current_Tenant_Only()
    {
        CreateTable("sync.db");
        var store = new SQLiteStore<TenantCounter>();
        store.SetSettings(new SqLiteSettings(_root, "sync.db"));
        store.Create(Row(Ours, "Ours"));
        store.Create(Row(Theirs, "Theirs"));

        new TenantBulkStoreWrapper<SQLiteStore<TenantCounter>, TenantCounter>(store, CtxFor(Ours, "Ours"))
            .Update(x => x.Name == "shared", new PropertyUpdate<TenantCounter>().Increment(x => x.Hits, 5));

        store.ReadFirst(x => x.TenantGuid == Ours)!.Hits.Should().Be(15);
        store.ReadFirst(x => x.TenantGuid == Theirs)!.Hits.Should().Be(10);
    }

    [Fact]
    public async Task InMemory_Increment_Through_ApplyTo_Applies_To_The_Current_Tenant_Only()
    {
        var store = new AsyncInMemoryStore<TenantCounter>();
        var ours = await store.CreateAsync(Row(Ours, "Ours"));
        var theirs = await store.CreateAsync(Row(Theirs, "Theirs"));

        await new AsyncTenantBulkStoreWrapper<AsyncInMemoryStore<TenantCounter>, TenantCounter>(store, CtxFor(Ours, "Ours"))
            .UpdateAsync(x => x.Name == "shared", new PropertyUpdate<TenantCounter>().Increment(x => x.Hits, 5));

        (await store.ReadAsync(ours))!.Hits.Should().Be(15);
        (await store.ReadAsync(theirs))!.Hits.Should().Be(10);
    }
}
