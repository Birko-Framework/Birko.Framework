using System;
using System.Linq;
using System.Threading;
using Birko.Data.Stores;
using Birko.Data.Sync;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-H099: result.Success was `Errors.Count == 0 || IsCancellationRequested`, so a sync that
/// recorded per-item errors and was then cancelled reported Success=true, hiding the failures.
/// Success is now driven by errors only.
/// </summary>
public class SyncSuccessOnCancellationTests
{
    /// <summary>Local store that fails every create, so downloading a remote item records an error.</summary>
    private sealed class FailingCreateStore : TestBulkStore
    {
        protected override Guid CreateCore(TestSyncModel data, StoreDataDelegate<TestSyncModel>? storeDelegate = null)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void CancelledSync_WithErrors_IsNotSuccess()
    {
        var local = new FailingCreateStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime("Default", DateTime.UtcNow.AddDays(-1));
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "Remote" });

        using var cts = new CancellationTokenSource();
        var options = new SyncOptions
        {
            Direction = SyncDirection.Download,
            CancellationToken = cts.Token,
            // Cancel once the first batch (with its recorded errors) has completed, before the loop
            // re-checks cancellation and before Success is computed.
            OnBatchCompleted = _ => cts.Cancel(),
        };

        var result = provider.Sync(options);

        result.Errors.Should().NotBeEmpty();
        result.Success.Should().BeFalse("errors must force Success=false even when the run was cancelled");
    }
}
