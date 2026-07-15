using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Sync;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Sync.Tenant.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// CR-H105: CreateKnowledgeItem inferred IsLocalDeleted/IsRemoteDeleted from mere fetch-absence, so a
/// first-seen one-sided (or predicate-excluded) item was persisted as "deleted", later driving
/// spurious conflicts / erroneous deletes. It now requires positive evidence (previously known AND
/// now absent). CR-H106: ApplyConflictResolutionAsync swallowed every exception, so a failed conflict
/// write was silently lost and the sync still reported success; the swallowing catch is removed.
/// </summary>
public class TenantSyncKnowledgeAndConflictTests
{
    private class Item : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class FakeKnowledgeStore : ISyncKnowledgeStore
    {
        public Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<Guid, ISyncKnowledgeItem>());
        public Task<DateTime?> GetLastSyncTimeAsync(string scope, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);
        public Task UpdateKnowledgeAsync(IEnumerable<ISyncKnowledgeItem> items, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SetLastSyncTimeAsync(string scope, Guid? tenantId, DateTime syncTime, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingUpdateStore : AsyncInMemoryStore<Item>
    {
        protected override Task UpdateCoreAsync(Item data, StoreDataDelegate<Item>? processDelegate = null, CancellationToken ct = default)
            => throw new InvalidOperationException("update failed");
    }

    private sealed class CancelObservingUpdateStore : AsyncInMemoryStore<Item>
    {
        protected override Task UpdateCoreAsync(Item data, StoreDataDelegate<Item>? processDelegate = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return base.UpdateCoreAsync(data, processDelegate, ct);
        }
    }

    // ---- CR-H105 ----

    [Fact]
    public void FirstSeenOneSidedItem_IsNotFlaggedDeleted()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<Item>, Item>(
            new AsyncInMemoryStore<Item>(), new AsyncInMemoryStore<Item>(), new FakeKnowledgeStore());

        var local = new Item { Guid = Guid.NewGuid(), Name = "only-local" };
        var k = provider.CreateKnowledgeItem(local.Guid!.Value, local, null, hasKnowledge: false, new SyncOptions { Scope = "S" });

        k.IsRemoteDeleted.Should().BeFalse("absent-on-remote for a never-synced item is a one-sided create, not a deletion");
        k.IsLocalDeleted.Should().BeFalse();
    }

    [Fact]
    public void PreviouslyKnownItemNowAbsent_IsFlaggedDeleted()
    {
        var provider = new TenantSyncProvider<AsyncInMemoryStore<Item>, Item>(
            new AsyncInMemoryStore<Item>(), new AsyncInMemoryStore<Item>(), new FakeKnowledgeStore());

        var guid = Guid.NewGuid();
        var local = new Item { Guid = guid, Name = "still-local" };
        var k = provider.CreateKnowledgeItem(guid, local, null, hasKnowledge: true, new SyncOptions { Scope = "S" });

        k.IsRemoteDeleted.Should().BeTrue("a previously-synced item now gone from remote is a genuine deletion");
        k.IsLocalDeleted.Should().BeFalse();
    }

    // ---- CR-H106 ----

    [Fact]
    public async Task ConflictResolution_WriteFailure_Propagates()
    {
        var provider = new TenantSyncProvider<ThrowingUpdateStore, Item>(
            new ThrowingUpdateStore(), new ThrowingUpdateStore(), new FakeKnowledgeStore());

        var remoteItem = new Item { Guid = Guid.NewGuid(), Name = "remote" };
        var progress = new SyncProgress();

        // UseRemote writes to the local store, which throws — the exception must propagate (so the
        // caller's per-item catch records a SyncError) rather than being swallowed.
        Func<Task> act = () => provider.ApplyConflictResolutionAsync(
            ConflictResolution.UseRemote, remoteItem.Guid!.Value, null, remoteItem,
            new SyncFilterOptions<Item>(), progress);

        await act.Should().ThrowAsync<InvalidOperationException>();
        progress.UpdatedItems.Should().Be(0);
    }

    // ---- CR-L222: the conflict-resolution write must observe the forwarded CancellationToken ----

    [Fact]
    public async Task ConflictResolution_ForwardsCancellationToken_ToUpdate()
    {
        var provider = new TenantSyncProvider<CancelObservingUpdateStore, Item>(
            new CancelObservingUpdateStore(), new CancelObservingUpdateStore(), new FakeKnowledgeStore());

        var remoteItem = new Item { Guid = Guid.NewGuid(), Name = "remote" };
        var progress = new SyncProgress();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Before the fix the internal UpdateAsync got the default token, so the store never observed the
        // cancellation and the write succeeded. Now the token flows through and the update throws.
        Func<Task> act = () => provider.ApplyConflictResolutionAsync(
            ConflictResolution.UseRemote, remoteItem.Guid!.Value, null, remoteItem,
            new SyncFilterOptions<Item>(), progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        progress.UpdatedItems.Should().Be(0);
    }
}
