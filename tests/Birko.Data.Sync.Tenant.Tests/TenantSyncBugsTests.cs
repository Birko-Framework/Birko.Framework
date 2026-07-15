using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Sync;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Sync.Tenant.Models;
using Birko.Data.Sync.Tenant.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// Regressions for the three TenantSyncProvider bugs:
/// CR-M167 (preview swallowed all failures as a spurious conflict),
/// CR-M168 (initial sync mutated the caller's SyncOptions.Direction + reported the wrong direction),
/// CR-M169 (an item with an unset TenantGuid passed every tenant's filter — cross-tenant leak).
/// </summary>
public class TenantSyncBugsTests
{
    private class Item : AbstractModel
    {
        public string? Name { get; set; }
    }

    // Has a TenantGuid property, so BelongsToTenant engages the tenant check.
    private class TenantItem : AbstractModel
    {
        public Guid? TenantGuid { get; set; }
        public string? Name { get; set; }
    }

    private sealed class FakeKnowledgeStore : ISyncKnowledgeStore
    {
        public Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<Guid, ISyncKnowledgeItem>());
        public Task<DateTime?> GetLastSyncTimeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null); // null ⇒ initial sync
        public Task UpdateKnowledgeAsync(IEnumerable<ISyncKnowledgeItem> items, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SetLastSyncTimeAsync(string scope, Guid? tenantId, DateTime syncTime, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingReadStore : AsyncInMemoryStore<Item>
    {
        public override Task<IEnumerable<Item>> ReadAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("read failed");
    }

    // ---- CR-M169: unset TenantGuid must NOT pass a tenant's filter ----

    [Fact]
    public void BelongsToTenant_ExcludesUnsetOrMismatchedTenant()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>(
            new AsyncInMemoryStore<TenantItem>(), new AsyncInMemoryStore<TenantItem>(), new FakeKnowledgeStore());
        var belongs = typeof(TenantSyncProvider<AsyncInMemoryStore<TenantItem>, TenantItem>)
            .GetMethod("BelongsToTenant", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var tenant = Guid.NewGuid();
        bool Belongs(TenantItem it) => (bool)belongs.Invoke(provider, new object[] { it, tenant })!;

        Belongs(new TenantItem { TenantGuid = tenant }).Should().BeTrue("matching tenant is in scope");
        Belongs(new TenantItem { TenantGuid = Guid.NewGuid() }).Should().BeFalse("a different tenant is excluded");
        Belongs(new TenantItem { TenantGuid = null }).Should().BeFalse("CR-M169: an unset TenantGuid must NOT leak into every tenant");
    }

    [Fact]
    public void BelongsToTenant_AllowsTypesWithoutTenantProperty()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<Item>, Item>(
            new AsyncInMemoryStore<Item>(), new AsyncInMemoryStore<Item>(), new FakeKnowledgeStore());
        var belongs = typeof(TenantSyncProvider<AsyncInMemoryStore<Item>, Item>)
            .GetMethod("BelongsToTenant", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Item has no TenantGuid property → the allow-all path applies (not a tenant-scoped entity).
        ((bool)belongs.Invoke(provider, new object[] { new Item(), Guid.NewGuid() })!).Should().BeTrue();
    }

    // ---- CR-M168: no mutation of the caller's options; result reports the effective direction ----

    [Fact]
    public async Task InitialSync_DoesNotMutateCallerDirection_AndReportsEffectiveDirection()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<Item>, Item>(
            new AsyncInMemoryStore<Item>(), new AsyncInMemoryStore<Item>(), new FakeKnowledgeStore());

        // A TenantSyncOptions instance is returned as-is by ApplyTenantContext, so the old in-place
        // mutation of Direction would leak back to this caller-held object.
        var options = new TenantSyncOptions { Scope = "S", Direction = SyncDirection.Bidirectional };

        var result = await provider.SyncAsync(options);

        options.Direction.Should().Be(SyncDirection.Bidirectional, "CR-M168: the caller's options must not be mutated");
        result.IsInitialSync.Should().BeTrue();
        result.Direction.Should().Be(SyncDirection.Download, "CR-M168: the result must report the direction that actually ran");
    }

    // ---- CR-M167: preview failures propagate instead of masquerading as a conflict ----

    [Fact]
    public async Task Preview_StoreReadFailure_Propagates_NotSwallowedAsConflict()
    {
        var provider = new TenantSyncProvider<ThrowingReadStore, Item>(
            new ThrowingReadStore(), new ThrowingReadStore(), new FakeKnowledgeStore());

        Func<Task> act = () => provider.PreviewAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("read failed");
    }

    // ---- CR-L222: cancellation must stop the OUTER batch loop, not only the inner item loop ----

    [Fact]
    public async Task Cancellation_StopsOuterBatchLoop_NotJustInnerLoop()
    {
        var local = new AsyncInMemoryStore<Item>();
        var remote = new AsyncInMemoryStore<Item>();
        // Three remote-only items at BatchSize 1 ⇒ three batches on an initial Download sync.
        for (var i = 0; i < 3; i++)
            await remote.CreateAsync(new Item { Guid = Guid.NewGuid(), Name = $"r{i}" });

        var provider = new TenantSyncProvider<AsyncInMemoryStore<Item>, Item>(local, remote, new FakeKnowledgeStore());

        using var cts = new CancellationTokenSource();
        var batchCallbacks = 0;
        var options = new TenantSyncOptions
        {
            Scope = "S",
            Direction = SyncDirection.Download,
            BatchSize = 1,
            CancellationToken = cts.Token,
            OnBatchCompleted = _ => { batchCallbacks++; cts.Cancel(); }
        };

        await provider.SyncAsync(options);

        // Fixed: the outer loop breaks after the first (now-cancelled) batch ⇒ exactly one callback.
        // Before the fix the inner break left the outer `for` running, firing a callback per batch (3).
        batchCallbacks.Should().Be(1, "cancellation must stop the outer batch loop promptly");
    }
}
