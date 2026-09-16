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
/// SH-H012 (TASK-309): <c>AsyncSyncProvider.SyncAsync</c> persisted sync knowledge with
/// <c>options.CancellationToken</c>. The batch loop <b>breaks</b> on cancellation rather than throwing, so
/// those three calls were reached with an already-cancelled token and threw immediately — the outer catch
/// recorded a generic "Sync failed" and the knowledge for every item <b>already written to the stores</b>
/// by completed batches was lost, leaving the next run to re-decide them blind.
/// <para>The synchronous <c>SyncProvider</c> passes no token and has always persisted, so the two halves of
/// one contract disagreed. The sync test below is therefore a <b>contract pin</b>, not evidence; the async
/// ones are the provers.</para>
/// </summary>
public class CancelledSyncKnowledgePersistenceTests
{
    private const string Scope = "Default";

    /// <summary>
    /// Three remote-only rows, one per batch, with the token cancelled the moment the first batch
    /// completes — so at least one row has been written to the local store before the loop breaks.
    /// </summary>
    private static SyncOptions CancelAfterFirstBatch(CancellationTokenSource cts) => new()
    {
        Direction = SyncDirection.Download,
        Scope = Scope,
        BatchSize = 1,
        CancellationToken = cts.Token,
        OnBatchCompleted = _ => cts.Cancel()
    };

    [Fact]
    public async Task SH_H012_A_cancelled_async_run_still_records_knowledge_for_what_it_wrote()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" + i }, null, CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = await provider.SyncAsync(CancelAfterFirstBatch(cts));

        // Observed state: the row that was written down has a knowledge row to match it. Before the fix
        // the knowledge store was empty even though the local store held the downloaded row.
        var written = (await local.ReadAsync(null, null, null, null, CancellationToken.None)).ToList();
        written.Should().NotBeEmpty("the first batch completed before cancellation was observed");

        var recorded = (await knowledge.ReadAsync(null, null, null, null, CancellationToken.None)).ToList();
        recorded.Should().HaveCount(written.Count);
        recorded.Select(k => k.EntityGuid).Should().BeEquivalentTo(written.Select(w => w.Guid!.Value));
    }

    [Fact]
    public async Task SH_H012_A_cancelled_async_run_still_records_the_last_sync_time()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        var before = DateTime.UtcNow.AddDays(-1);
        await knowledge.SetLastSyncTimeAsync(Scope, before, CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" + i }, null, CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        await provider.SyncAsync(CancelAfterFirstBatch(cts));

        (await knowledge.GetLastSyncTimeAsync(Scope, CancellationToken.None))
            .Should().BeAfter(before, "SetLastSyncTimeAsync also used to throw on the cancelled token");
    }

    [Fact]
    public async Task SH_H012_A_cancelled_async_run_does_not_report_a_generic_Sync_failed_error()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" + i }, null, CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var provider = new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        var result = await provider.SyncAsync(CancelAfterFirstBatch(cts));

        // The cancellation is a normal early stop, not a failure of the sync. It used to surface as
        // Message = "Sync failed" with an OperationCanceledException from the knowledge write.
        result.Errors.Should().NotContain(e => e.Message == "Sync failed");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void The_synchronous_provider_records_knowledge_for_a_cancelled_run_too()
    {
        // Contract pin, not evidence: SyncProvider never passed a token here and so was never broken.
        // It is the behaviour the async twin was made to match, so it must keep holding.
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime(Scope, DateTime.UtcNow.AddDays(-1));

        for (var i = 0; i < 3; i++)
        {
            remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "r" + i });
        }

        using var cts = new CancellationTokenSource();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);
        provider.Sync(CancelAfterFirstBatch(cts));

        var written = local.Read(null, null, null, null).ToList();
        written.Should().NotBeEmpty();
        knowledge.Read(null, null, null, null).Should().HaveCount(written.Count);
    }
}
