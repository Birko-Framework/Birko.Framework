using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// The four interlocking findings that made the DEFAULT sync direction lose data (TASK-309):
/// <list type="bullet">
/// <item><b>SH-H008</b> — <c>ProcessBatch</c>'s <c>SyncAction.Create</c> arm tested
/// <c>Direction == Download</c> then <c>== Upload</c> with no else. <c>SyncOptions.Direction</c> defaults
/// to <c>Bidirectional</c>, for which <c>DetermineSyncAction</c> does return <c>Create</c> — so nothing was
/// written, while the item was still counted <c>Processed</c> and a knowledge row still emitted.</item>
/// <item><b>SH-H011</b> — the knowledge row was built from the PRE-action items, so after a create the
/// destination's version hash was still null and the backend set <c>Is*Deleted = true</c>. Those flags mean
/// "absent when decided", but the delete branches read them as "deleted".</item>
/// <item><b>SH-H009</b> — the two together: a brand-new local-only item got a knowledge row saying it was
/// deleted remotely, and the SECOND run acted on that. Under <c>RemoteWins</c> the local row was destroyed.</item>
/// <item><b>SH-H010</b> — the conflict escape from that state was itself a no-op: every arm of
/// <c>ApplyConflictResolution</c> required BOTH items to be non-null, and a conflict is only ever raised
/// when one of them is null by construction.</item>
/// </list>
/// Every assertion here is <b>observed state</b> — rows counted and read back from the stores — never
/// "no exception was thrown", per this task's acceptance criteria.
/// </summary>
public class BidirectionalCreateAndConflictTests
{
    private const string Scope = "Default";

    private static SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge> Provider(
        TestBulkStore local, TestBulkStore remote, TestSyncKnowledgeItemStore knowledge)
        => new(local, remote, knowledge);

    /// <summary>Marks the scope as already synced once, so DetermineSyncAction leaves the initial-sync branch.</summary>
    private static void NotInitialSync(TestSyncKnowledgeItemStore knowledge)
        => knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));

    private static void NotInitialSyncAsync(TestAsyncSyncKnowledgeItemStore knowledge)
        => knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None).GetAwaiter().GetResult();

    private static TestSyncKnowledge SoleKnowledge(TestSyncKnowledgeItemStore knowledge)
        => knowledge.Read(null, null, null, null).Should().ContainSingle().Subject;

    // ---------------------------------------------------------------- SH-H008

    [Fact]
    public void SH_H008_Bidirectional_uploads_a_new_local_item_instead_of_dropping_it()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "Only local" });

        var result = Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope });

        result.Success.Should().BeTrue();
        // Observed state: the row is actually in the remote store, not merely "no exception".
        remote.Count().Should().Be(1);
        remote.Read(x => x.Guid == guid, null, null, null).Should().ContainSingle()
            .Which.Name.Should().Be("Only local");
        result.Created.Should().Be(1);
    }

    [Fact]
    public void SH_H008_Bidirectional_downloads_a_new_remote_item_instead_of_dropping_it()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        remote.Create(new TestSyncModel { Guid = guid, Name = "Only remote" });

        var result = Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope });

        result.Success.Should().BeTrue();
        local.Count().Should().Be(1);
        local.Read(x => x.Guid == guid, null, null, null).Should().ContainSingle()
            .Which.Name.Should().Be("Only remote");
        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task SH_H008_Async_bidirectional_uploads_a_new_local_item()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        NotInitialSyncAsync(knowledge);

        var guid = Guid.NewGuid();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "Only local" }, null, CancellationToken.None);

        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = await provider.SyncAsync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope });

        result.Success.Should().BeTrue();
        (await remote.CountAsync(null, CancellationToken.None)).Should().Be(1);
        result.Created.Should().Be(1);
    }

    /// <summary>
    /// The drop was invisible in every counter: the item was Processed but neither Created nor Skipped.
    /// Pinning that arithmetic is what stops a future edit reintroducing a no-op arm that "looks busy".
    /// </summary>
    [Fact]
    public void SH_H008_A_bidirectional_create_is_never_counted_as_processed_without_landing_somewhere()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "a" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "b" });

        var result = Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope });

        result.TotalProcessed.Should().Be(2);
        (result.Created + result.Updated + result.Deleted + result.Skipped).Should().Be(result.TotalProcessed);
        result.Created.Should().Be(2);
    }

    // ---------------------------------------------------------------- SH-H011

    [Fact]
    public void SH_H011_Knowledge_after_an_upload_records_the_remote_as_present_not_deleted()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "Only local" });

        Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Bidirectional, Scope = Scope });

        var row = SoleKnowledge(knowledge);
        row.IsRemoteDeleted.Should().BeFalse("the remote copy was just created by this very run");
        row.RemoteVersion.Should().NotBeNullOrEmpty();
        row.IsLocalDeleted.Should().BeFalse();
    }

    [Fact]
    public void SH_H011_Knowledge_after_a_download_records_the_local_as_present_not_deleted()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "Only remote" });

        Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        var row = SoleKnowledge(knowledge);
        row.IsLocalDeleted.Should().BeFalse("the local copy was just created by this very run");
        row.LocalVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SH_H011_Knowledge_after_a_delete_does_record_the_deleted_side()
    {
        // The other half of the flag's meaning: when a row really was removed, the flag must still be set.
        // Without this, "never mark deleted" would pass the test above and break delete propagation.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "doomed" });
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        }, null);

        Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        local.Count().Should().Be(0);
        knowledge.Read(x => x.EntityGuid == guid, null, null, null)
            .Should().ContainSingle().Which.IsLocalDeleted.Should().BeTrue();
    }

    // ---------------------------------------------------------------- SH-H009

    [Theory]
    [InlineData(ConflictResolutionPolicy.RemoteWins)]
    [InlineData(ConflictResolutionPolicy.LocalWins)]
    [InlineData(ConflictResolutionPolicy.NewestWins)]
    public void SH_H009_A_new_local_item_survives_a_second_bidirectional_run(ConflictResolutionPolicy policy)
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "brand new" });

        var options = new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = policy
        };

        var provider = Provider(local, remote, knowledge);
        provider.Sync(options);
        var second = provider.Sync(options);

        // Before the fix: run 1 wrote nothing and left IsRemoteDeleted = true, so run 2 routed into the
        // deletion branch. Under RemoteWins the local row was DELETED; under the other two it was lost or
        // stuck forever. All three now converge on "present on both sides".
        second.Success.Should().BeTrue();
        local.Count().Should().Be(1);
        remote.Count().Should().Be(1);
        local.Read(x => x.Guid == guid, null, null, null).Should().ContainSingle();
        remote.Read(x => x.Guid == guid, null, null, null).Should().ContainSingle();
        second.Deleted.Should().Be(0);
    }

    // ---------------------------------------------------------------- SH-H010

    [Fact]
    public void SH_H010_A_conflict_resolved_UseLocal_recreates_the_missing_remote()
    {
        // Local modified, remote deleted, NewestWins with only a local timestamp -> UseLocal. The
        // resolution has to RE-CREATE on the remote, which is what DetermineSyncAction's LocalWins
        // shortcut already does for the identical state.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "survivor" });
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        }, null);

        var result = Provider(local, remote, knowledge).Sync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.NewestWins
        });

        result.Conflicts.Should().Be(1);
        remote.Count().Should().Be(1, "UseLocal must re-create the row the remote had lost");
        local.Count().Should().Be(1);
        result.Created.Should().Be(1);
    }

    [Fact]
    public void SH_H010_A_conflict_resolved_UseRemote_recreates_the_missing_local()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        remote.Create(new TestSyncModel { Guid = guid, Name = "survivor" });
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsLocalDeleted = true
        }, null);

        var result = Provider(local, remote, knowledge).Sync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.NewestWins
        });

        result.Conflicts.Should().Be(1);
        local.Count().Should().Be(1, "UseRemote must re-create the row the local side had lost");
        result.Created.Should().Be(1);
    }

    [Fact]
    public void SH_H010_A_custom_resolver_choosing_the_deleted_side_propagates_the_deletion()
    {
        // Local modified, remote deleted, and the caller's resolver says UseRemote — i.e. honour the
        // deletion. The surviving local row must go, matching DetermineSyncAction's RemoteWins shortcut.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "doomed" });
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        }, null);

        var result = Provider(local, remote, knowledge).Sync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.Custom,
            CustomConflictResolver = _ => ConflictResolution.UseRemote
        });

        result.Conflicts.Should().Be(1);
        local.Count().Should().Be(0, "the caller chose the side on which the row was deleted");
        result.Deleted.Should().Be(1);
    }

    [Fact]
    public void SH_H010_Skip_still_leaves_both_sides_untouched()
    {
        // Contract pin, not evidence: Skip was the one resolution that already behaved, and it must keep
        // behaving now that the neighbouring arms write.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "left alone" });
        knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        }, null);

        var result = Provider(local, remote, knowledge).Sync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.Custom,
            CustomConflictResolver = _ => ConflictResolution.Skip
        });

        local.Count().Should().Be(1);
        remote.Count().Should().Be(0);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task SH_H010_Async_conflict_resolved_UseLocal_recreates_the_missing_remote()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        NotInitialSyncAsync(knowledge);

        var guid = Guid.NewGuid();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "survivor" }, null, CancellationToken.None);
        await knowledge.CreateAsync(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        }, null, CancellationToken.None);

        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = await provider.SyncAsync(new SyncOptions
        {
            Direction = SyncDirection.Bidirectional,
            Scope = Scope,
            ConflictPolicy = ConflictResolutionPolicy.NewestWins
        });

        result.Conflicts.Should().Be(1);
        (await remote.CountAsync(null, CancellationToken.None)).Should().Be(1);
        result.Created.Should().Be(1);
    }

    // ------------------------------------------- contract pins (not evidence)

    [Fact]
    public void Download_only_still_creates_locally_and_never_touches_the_remote()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" });
        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "l" });

        Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        local.Count().Should().Be(2, "the remote-only row came down");
        remote.Count().Should().Be(1, "Download must never push the local-only row up");
    }

    [Fact]
    public void Upload_only_still_creates_remotely_and_never_touches_the_local()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        NotInitialSync(knowledge);

        local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "l" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" });

        Provider(local, remote, knowledge)
            .Sync(new SyncOptions { Direction = SyncDirection.Upload, Scope = Scope });

        remote.Count().Should().Be(2);
        local.Count().Should().Be(1);
    }
}
