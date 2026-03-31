using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.Sync.Tests;

public class SyncProviderTests
{
    #region Constructor

    [Fact]
    public void Constructor_NullLocalStore_Throws()
    {
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();

        var act = () => new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(null!, remote, knowledge);

        act.Should().Throw<ArgumentNullException>().WithParameterName("localStore");
    }

    [Fact]
    public void Constructor_NullRemoteStore_Throws()
    {
        var local = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();

        var act = () => new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, null!, knowledge);

        act.Should().Throw<ArgumentNullException>().WithParameterName("remoteStore");
    }

    [Fact]
    public void Constructor_NullKnowledgeStore_Throws()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();

        var act = () => new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("knowledgeStore");
    }

    #endregion

    #region Sync — Initial

    [Fact]
    public void Sync_InitialSync_DownloadsAllRemote()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var item1 = new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" };
        var item2 = new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" };
        remote.Create(item1);
        remote.Create(item2);

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.Success.Should().BeTrue();
        result.IsInitialSync.Should().BeTrue();
        result.Created.Should().Be(2);
        local.Count().Should().Be(2);
    }

    #endregion

    #region Sync — Download

    [Fact]
    public void Sync_Download_CreatesLocalFromRemote()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        // Simulate a previous sync so it's not initial
        knowledge.SetLastSyncTime("Default", DateTime.UtcNow.AddDays(-1));
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var remoteItem = new TestSyncModel { Guid = Guid.NewGuid(), Name = "Remote Item" };
        remote.Create(remoteItem);

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.Success.Should().BeTrue();
        result.Created.Should().Be(1);
        local.Read(remoteItem.Guid!.Value).Should().NotBeNull();
    }

    [Fact]
    public void Sync_Download_UpdatesLocalFromRemote()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime("Default", DateTime.UtcNow.AddDays(-1));
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var guid = Guid.NewGuid();
        local.Create(new TestSyncModel { Guid = guid, Name = "Old" });
        remote.Create(new TestSyncModel { Guid = guid, Name = "New" });

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.Updated.Should().Be(1);
        local.Read(guid)!.Name.Should().Be("New");
    }

    #endregion

    #region Sync — Upload

    [Fact]
    public void Sync_Upload_CreatesRemoteFromLocal()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        knowledge.SetLastSyncTime("Default", DateTime.UtcNow.AddDays(-1));
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        var localItem = new TestSyncModel { Guid = Guid.NewGuid(), Name = "Local Item" };
        local.Create(localItem);

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Upload });

        result.Success.Should().BeTrue();
        result.Created.Should().Be(1);
        remote.Read(localItem.Guid!.Value).Should().NotBeNull();
    }

    #endregion

    #region Sync — Result Counts

    [Fact]
    public void Sync_ReturnsCorrectCounts()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        // Initial sync with 3 remote items
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "C" });

        var result = provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        result.TotalProcessed.Should().Be(3);
        result.Created.Should().Be(3);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region Sync — Knowledge

    [Fact]
    public void Sync_UpdatesSyncKnowledge()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "Item" });

        provider.Sync(new SyncOptions { Direction = SyncDirection.Download });

        knowledge.GetLastSyncTime("Default").Should().NotBeNull();
    }

    #endregion

    #region Preview

    [Fact]
    public void Preview_ReturnsCorrectActionCounts()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });
        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" });

        var preview = provider.Preview(new SyncOptions { Direction = SyncDirection.Download });

        preview.ToCreate.Should().Be(2);
        preview.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Preview_DoesNotModifyStores()
    {
        var local = new TestBulkStore();
        var remote = new TestBulkStore();
        var knowledge = new TestSyncKnowledgeItemStore();
        var provider = new SyncProvider<TestBulkStore, TestSyncModel, TestSyncKnowledge>(local, remote, knowledge);

        remote.Create(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });

        provider.Preview(new SyncOptions { Direction = SyncDirection.Download });

        local.Count().Should().Be(0); // Local store should remain empty
    }

    #endregion
}
