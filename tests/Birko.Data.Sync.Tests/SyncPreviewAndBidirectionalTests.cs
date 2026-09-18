using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Birko.Data.Stores;
using Birko.Data.Sync;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-M155: Preview wrapped its whole body in a bare catch that turned any failure — including
/// OperationCanceledException — into a normal SyncPreview with Conflicts++. Cancellation now propagates.
/// CR-M156: the bidirectional both-exist branch had a dead `if (winner == "conflict")` block
/// (GetWinner never returns "conflict"); it now simply applies the policy winner as an Update.
/// </summary>
public class SyncPreviewAndBidirectionalTests
{
    /// <summary>Store whose bulk read throws OperationCanceledException (stands in for a cancelled read).</summary>
    private sealed class CancellingStore : TestBulkStore
    {
        protected override IEnumerable<TestSyncModel> ReadCore(
            Expression<Func<TestSyncModel, bool>>? filter = null, OrderBy<TestSyncModel>? orderBy = null,
            int? limit = null, int? offset = null)
            => throw new OperationCanceledException();
    }

    [Fact]
    public void Preview_propagates_cancellation_instead_of_masking_it_as_a_conflict()
    {
        var local = new CancellingStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        Action act = () => provider.Preview(new SyncOptions { Direction = SyncDirection.Bidirectional });

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Bidirectional_both_exist_is_an_update_not_a_conflict()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime("Default", DateTime.UtcNow.AddDays(-1)); // non-initial sync
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "local" });
        remote.Create(new TestSyncModel { Guid = guid, Name = "remote" });

        var preview = provider.Preview(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            ConflictPolicy = ConflictResolutionPolicy.LocalWins,
        });

        preview.Conflicts.Should().Be(0, "CR-M156: both-exist applies the policy winner, it is not a conflict");
        preview.Items.Should().Contain(i => i.Guid == guid && i.Action == SyncAction.Update);
    }
}
