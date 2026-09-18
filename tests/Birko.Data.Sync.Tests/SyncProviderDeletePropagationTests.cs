using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-M157: delete-propagation coverage for the (sync) <see cref="SyncProvider{TStore,T,TKnowledge}"/>.
/// A tombstone is a knowledge item whose IsRemoteDeleted / IsLocalDeleted flag is set for an entity
/// that still exists on the other side; the provider must then delete the surviving copy rather than
/// re-create the deleted one.
/// </summary>
public class SyncProviderDeletePropagationTests
{
    private const string Scope = "Default";

    private static void SeedTombstone(TestSyncKnowledgeItemStore knowledge, Guid guid, bool remoteDeleted, bool localDeleted)
    {
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = remoteDeleted,
            IsLocalDeleted = localDeleted
        }, null);
    }

    [Fact]
    public void Download_RemoteDeletedTombstone_DeletesLocalCopy()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1)); // not initial sync

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "Local survivor" });
        // remote does NOT have it; a tombstone records it was deleted remotely.
        SeedTombstone(knowledge, guid, remoteDeleted: true, localDeleted: false);

        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        result.Success.Should().BeTrue();
        result.Deleted.Should().Be(1);
        local.Count().Should().Be(0); // local copy propagated-deleted
    }

    [Fact]
    public void Download_NoTombstone_KeepsLocalOnlyItem()
    {
        // Without a remote-deleted tombstone, a local-only item in Download mode is a no-op (Skip),
        // NOT a delete — proves the delete is gated on the tombstone flag, not mere absence.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "Local only" });

        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        result.Deleted.Should().Be(0);
        local.Count().Should().Be(1);
    }

    [Fact]
    public void Upload_LocalDeletedTombstone_DeletesRemoteCopy()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));

        var guid = Guid.NewGuid();
        remote.Create(new TestSyncModel { Guid = guid, Name = "Remote survivor" });
        // local does NOT have it; a tombstone records it was deleted locally.
        SeedTombstone(knowledge, guid, remoteDeleted: false, localDeleted: true);

        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Upload, Scope = Scope });

        result.Success.Should().BeTrue();
        result.Deleted.Should().Be(1);
        remote.Count().Should().Be(0);
    }

    [Fact]
    public void Preview_RemoteDeletedTombstone_ReportsToDelete()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "Local survivor" });
        SeedTombstone(knowledge, guid, remoteDeleted: true, localDeleted: false);

        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var preview = provider.Preview(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        preview.ToDelete.Should().Be(1);
        // Preview is read-only — the survivor must still be present.
        local.Count().Should().Be(1);
        local.Read(guid).Should().NotBeNull("preview is read-only");
    }
}
