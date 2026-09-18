using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-L207: SyncOptions.MaxItems now caps the set processed per run. (CR-L209's empty-Guid guard is
/// code-review verified — the InMemory test store auto-assigns Guids on Create, so empty-Guid entities
/// can't be staged through it.)
/// </summary>
public class SyncOptionsAndGuidGuardTests
{
    private static SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge> NewProvider(
        out TestBulkStore local, out TestBulkStore remote)
    {
        local = new TestBulkStore();
        remote = new TestBulkStore();
        return new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(
            local, remote, new TestSyncKnowledgeItemStore());
    }

    [Fact]
    public void Sync_MaxItems_CapsTheNumberOfItemsProcessed()
    {
        var provider = NewProvider(out var local, out var remote);
        for (int i = 0; i < 3; i++)
            remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "R" + i });

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download, MaxItems = 2 });

        result.Success.Should().BeTrue();
        result.Created.Should().Be(2);
        local.Count().Should().Be(2);
    }

    [Fact]
    public void Sync_MaxItemsNull_ProcessesEverything()
    {
        var provider = NewProvider(out var local, out var remote);
        for (int i = 0; i < 3; i++)
            remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "R" + i });

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.Created.Should().Be(3);
        local.Count().Should().Be(3);
    }
}
