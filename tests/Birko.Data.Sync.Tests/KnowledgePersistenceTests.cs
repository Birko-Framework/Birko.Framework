using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// Regression tests for CR-C18: sync knowledge was persisted via Update on records whose store PK
/// (AbstractModel.Guid) was null, which is a no-op on a real bulk store — so first-run knowledge was
/// never inserted and every subsequent run started from empty knowledge. The provider now inserts
/// new knowledge (letting the store assign a PK) and updates existing rows by their real PK.
/// </summary>
public class KnowledgePersistenceTests
{
    [Fact]
    public void Sync_PersistsKnowledge_SoItIsReadableAfterwards()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" });

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.Success.Should().BeTrue();
        // Knowledge must actually be inserted (was a silent no-op before the fix).
        knowledge.Count().Should().Be(2);
        knowledge.Read(k => true, null, null, null).Should().OnlyContain(k => k.Guid.HasValue);
    }

    [Fact]
    public void Sync_RunTwice_UpsertsKnowledgeWithoutDuplicating()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        remote.Create(new TestSyncModel { Guid = g1, Name = "A" });
        remote.Create(new TestSyncModel { Guid = g2, Name = "B" });

        provider.Sync(new SyncOptions { Direction = SyncDirection.Download });
        provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        // The second run must update the existing rows, not insert duplicates.
        knowledge.Count().Should().Be(2);
        knowledge.Read(k => true, null, null, null).Select(k => k.EntityGuid)
            .Should().BeEquivalentTo(new[] { g1, g2 });
    }
}
