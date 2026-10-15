using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// TASK-157, the data-sync medium findings, plus two defects the triage measured beyond them. Every test runs the real
/// provider over two in-memory stores and asserts what the stores hold afterwards.
/// </summary>
public class SyncEngineTriageTests
{
    private const string Scope = "Default";
    private static readonly DateTime T0 = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Rig
    {
        public TestBulkStore Local { get; } = new();
        public TestBulkStore Remote { get; } = new();
        public TestSyncKnowledgeItemStore Knowledge { get; } = new();
        public SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge> Provider { get; }

        public Rig() => Provider = new(Local, Remote, Knowledge);

        public SyncResult Sync(ConflictResolutionPolicy policy = ConflictResolutionPolicy.NewestWins, SyncFilterOptions<TestSyncModel>? filter = null,
            Func<ConflictInfo, ConflictResolution>? resolver = null, SyncDirection direction = SyncDirection.Bidirectional)
            => Provider.Sync(new SyncOptions { Scope = Scope, ConflictPolicy = policy, CustomConflictResolver = resolver, Direction = direction }, filter);

        /// <summary>One item on both sides, recorded as a synced pair by a completed run.</summary>
        public Guid SyncedPair(string name = "item")
        {
            var guid = Guid.NewGuid();
            Remote.Create(new TestSyncModel { Guid = guid, Name = name, UpdatedAt = T0 });
            Sync().Errors.Should().BeEmpty();
            Local.Read(guid).Should().NotBeNull();
            return guid;
        }

        public void Edit(TestBulkStore store, Guid guid, string name, int hours)
        {
            var item = store.Read(guid)!;
            item.Name = name;
            item.UpdatedAt = T0.AddHours(hours);
            store.Update(item);
        }
    }

    // ------------------------------------------------ deletion is a recorded pair with one side missing

    [Fact]
    public void A_remote_deletion_after_a_synced_round_is_propagated_not_resurrected()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Remote.Delete(rig.Remote.Read(guid)!);

        var result = rig.Sync();

        result.Created.Should().Be(0, "the item was deleted remotely, not created locally");
        rig.Remote.Read(guid).Should().BeNull();
        rig.Local.Read(guid).Should().BeNull("the deletion propagates");
    }

    [Fact]
    public async Task An_async_local_deletion_after_a_synced_round_is_propagated()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var guid = Guid.NewGuid();
        await remote.CreateAsync(new TestSyncModel { Guid = guid, Name = "x", UpdatedAt = T0 });
        await provider.SyncAsync(new SyncOptions { Scope = Scope });
        await local.DeleteAsync((await local.ReadAsync(guid))!);

        await provider.SyncAsync(new SyncOptions { Scope = Scope });

        (await remote.ReadAsync(guid)).Should().BeNull();
        (await local.ReadAsync(guid)).Should().BeNull();
    }

    [Fact]
    public void A_local_only_item_present_at_the_first_sync_is_uploaded_by_the_second_not_deleted()
    {
        var rig = new Rig();
        var guid = Guid.NewGuid();
        rig.Local.Create(new TestSyncModel { Guid = guid, Name = "offline", UpdatedAt = T0 });

        rig.Sync(ConflictResolutionPolicy.RemoteWins);
        var second = rig.Sync(ConflictResolutionPolicy.RemoteWins);

        second.Deleted.Should().Be(0);
        rig.Local.Read(guid).Should().NotBeNull();
        rig.Remote.Read(guid).Should().NotBeNull();
    }

    // ------------------------------------------------ SH-M059: versions are compared

    [Fact]
    public void An_unchanged_pair_is_skipped_not_rewritten()
    {
        var rig = new Rig();
        rig.SyncedPair();

        var result = rig.Sync();

        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public void A_change_on_one_side_wins_whatever_the_policy()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Edit(rig.Remote, guid, "edited remotely", 1);

        rig.Sync(ConflictResolutionPolicy.LocalWins);

        rig.Local.Read(guid)!.Name.Should().Be("edited remotely", "only the remote side changed");
    }

    [Fact]
    public void A_change_on_both_sides_is_a_conflict_carrying_both_items()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Edit(rig.Local, guid, "local edit", 1);
        rig.Edit(rig.Remote, guid, "remote edit", 2);
        ConflictInfo? seen = null;

        var result = rig.Sync(ConflictResolutionPolicy.Custom, resolver: c => { seen = c; return ConflictResolution.UseLocal; });

        result.Conflicts.Should().Be(1);
        seen.Should().NotBeNull();
        seen!.LocalItem.Should().NotBeNull();
        seen.RemoteItem.Should().NotBeNull();
        rig.Remote.Read(guid)!.Name.Should().Be("local edit");
    }

    // ------------------------------------------------ SH-M049 / SH-M058: resolution failures are errors

    [Fact]
    public void Merge_is_refused_as_an_error_and_not_recorded()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Edit(rig.Local, guid, "local edit", 1);
        rig.Edit(rig.Remote, guid, "remote edit", 2);

        var result = rig.Sync(ConflictResolutionPolicy.Custom, resolver: _ => ConflictResolution.Merge);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ItemGuid.Should().Be(guid);
        rig.Sync(ConflictResolutionPolicy.Custom, resolver: _ => ConflictResolution.UseRemote).Conflicts
            .Should().Be(1, "the conflict was not recorded as reconciled, so it is raised again");
    }

    [Fact]
    public void A_failure_while_resolving_a_conflict_fails_the_run()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Edit(rig.Local, guid, "local edit", 1);
        rig.Edit(rig.Remote, guid, "remote edit", 2);
        var filter = new SyncFilterOptions<TestSyncModel>
        {
            CanSaveToLocal = _ => false,
            OnSaveFilterBlock = SaveFilterBlockAction.ThrowException
        };

        var result = rig.Sync(ConflictResolutionPolicy.NewestWins, filter);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.ItemGuid.Should().Be(guid);
    }

    // ------------------------------------------------ SH-M056 / SH-M057: save filters

    [Fact]
    public void A_save_filter_guards_a_delete_too()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair("protected");
        rig.Remote.Delete(rig.Remote.Read(guid)!);
        var filter = new SyncFilterOptions<TestSyncModel> { CanSaveToLocal = x => x.Name != "protected" };

        var result = rig.Sync(filter: filter);

        rig.Local.Read(guid).Should().NotBeNull("the filter forbids writing that row locally, deletion included");
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public void A_blocked_update_is_counted_and_retried_on_the_next_run()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Edit(rig.Remote, guid, "edited remotely", 1);

        var blocked = rig.Sync(filter: new SyncFilterOptions<TestSyncModel> { CanSaveToLocal = _ => false });
        var next = rig.Sync();

        blocked.Skipped.Should().Be(1);
        next.Updated.Should().Be(1, "the blocked difference was not recorded as reconciled");
        rig.Local.Read(guid)!.Name.Should().Be("edited remotely");
    }

    // ------------------------------------------------ SH-M053 / SH-M054 / SH-M055: options

    [Fact]
    public void An_initial_sync_does_not_write_to_the_callers_options_and_reports_the_direction_used()
    {
        var rig = new Rig();
        rig.Remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "x", UpdatedAt = T0 });
        var options = new SyncOptions { Scope = Scope, Direction = SyncDirection.Bidirectional };

        var result = rig.Provider.Sync(options);

        options.Direction.Should().Be(SyncDirection.Bidirectional);
        result.Direction.Should().Be(SyncDirection.Download);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_batch_size_that_is_not_positive_is_refused(int batchSize)
    {
        var rig = new Rig();
        rig.Remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "x", UpdatedAt = T0 });

        var act = () => rig.Provider.Sync(new SyncOptions { Scope = Scope, BatchSize = batchSize });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_async_batch_size_that_is_not_positive_is_refused(int batchSize)
    {
        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(
            new TestAsyncBulkStore(), new TestAsyncBulkStore(), new TestAsyncSyncKnowledgeItemStore());

        var act = () => provider.SyncAsync(new SyncOptions { Scope = Scope, BatchSize = batchSize });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Repeated_capped_runs_converge()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++)
            rig.Remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" + i, UpdatedAt = T0 });

        for (var run = 0; run < 3; run++)
            rig.Provider.Sync(new SyncOptions { Scope = Scope, MaxItems = 2 });

        rig.Local.Count().Should().Be(5, "each capped run takes items that still need work");
    }

    // ------------------------------------------------ SH-M052: preview errors / SH-M064: duplicate knowledge

    [Fact]
    public void A_preview_that_fails_throws_instead_of_reporting_a_conflict()
    {
        var rig = new Rig();
        rig.Local.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "x", UpdatedAt = T0 });
        var filter = new SyncFilterOptions<TestSyncModel> { LocalFetchPredicate = x => Fail(x) };

        var act = () => rig.Provider.Preview(new SyncOptions { Scope = Scope }, filter);

        act.Should().Throw<InvalidOperationException>().WithMessage("store failure");
    }

    private static bool Fail(TestSyncModel _) => throw new InvalidOperationException("store failure");

    [Fact]
    public void Two_knowledge_rows_for_one_entity_do_not_break_the_scope()
    {
        var rig = new Rig();
        var guid = rig.SyncedPair();
        rig.Knowledge.Create(new TestSyncKnowledge
        {
            EntityGuid = guid, Scope = Scope, LastSyncedAt = T0.AddDays(-3), LocalVersion = "old", RemoteVersion = "old"
        }, null);

        var result = rig.Sync();

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ------------------------------------------------ SH-M050 / SH-M051: the queue

    [Fact]
    public async Task A_cancelled_wait_releases_no_permit_and_leaves_no_queue_record()
    {
        var queue = new SyncQueue(maxConcurrentSyncs: 1);
        var release = new TaskCompletionSource();
        var running = queue.EnqueueAsync("a", async () => { await release.Task; return 1; });
        using var cts = new CancellationTokenSource();
        var waiting = queue.EnqueueAsync("b", () => Task.FromResult(2), cts.Token);
        cts.Cancel();
        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();
        await running;

        queue.GetQueueLength("b").Should().Be(0);
        var concurrent = 0;
        var peak = 0;
        async Task<int> Op()
        {
            var now = Interlocked.Increment(ref concurrent);
            peak = Math.Max(peak, now);
            await Task.Delay(30);
            Interlocked.Decrement(ref concurrent);
            return 0;
        }
        await Task.WhenAll(queue.EnqueueAsync("c", Op), queue.EnqueueAsync("d", Op), queue.EnqueueAsync("e", Op));
        peak.Should().Be(1, "maxConcurrentSyncs is 1, and a cancelled waiter must not have added a permit");
    }

    [Fact]
    public async Task Two_runs_of_one_scope_never_overlap_even_with_spare_permits()
    {
        var queue = new SyncQueue(maxConcurrentSyncs: 2);
        var concurrent = 0;
        var peak = 0;
        async Task<int> Op()
        {
            var now = Interlocked.Increment(ref concurrent);
            peak = Math.Max(peak, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref concurrent);
            return 0;
        }

        await Task.WhenAll(queue.EnqueueAsync("same", Op), queue.EnqueueAsync("same", Op));

        peak.Should().Be(1);
    }
}
