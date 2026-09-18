using Birko.Data.Sync.Models;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-M157: the async <see cref="AsyncSyncProvider{TStore,T,TKnowledge}"/> was entirely untested
/// (only the sync provider was exercised). These assert async parity for the core paths — initial
/// download, download create/update, upload create, delete propagation, and preview.
/// </summary>
public class AsyncSyncProviderTests
{
    private const string Scope = "Default";

    private static AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge> NewProvider(
        TestAsyncBulkStore local, TestAsyncBulkStore remote, TestAsyncSyncKnowledgeItemStore knowledge)
        => new(local, remote, knowledge);

    #region Constructor

    [Fact]
    public void Constructor_NullLocalStore_Throws()
    {
        var act = () => new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(
            null!, new TestAsyncBulkStore(), new TestAsyncSyncKnowledgeItemStore());

        act.Should().Throw<ArgumentNullException>().WithParameterName("localStore");
    }

    [Fact]
    public void Constructor_NullKnowledgeStore_Throws()
    {
        var act = () => new AsyncSyncProvider<TestAsyncBulkStore, TestSyncModel, TestSyncKnowledge>(
            new TestAsyncBulkStore(), new TestAsyncBulkStore(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("knowledgeStore");
    }

    #endregion

    [Fact]
    public async Task SyncAsync_InitialSync_DownloadsAllRemote()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });
        await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" });

        var result = await NewProvider(local, remote, knowledge)
            .SyncAsync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        result.Success.Should().BeTrue();
        result.IsInitialSync.Should().BeTrue();
        result.Created.Should().Be(2);
        (await local.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_Download_UpdatesLocalFromRemote()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var guid = Guid.NewGuid();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "Old" });
        await remote.CreateAsync(new TestSyncModel { Guid = guid, Name = "New" });

        var result = await NewProvider(local, remote, knowledge)
            .SyncAsync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        result.Updated.Should().Be(1);
        (await local.ReadAsync(guid))!.Name.Should().Be("New");
    }

    [Fact]
    public async Task SyncAsync_Upload_CreatesRemoteFromLocal()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var localItem = new TestSyncModel { Guid = Guid.NewGuid(), Name = "Local" };
        await local.CreateAsync(localItem);

        var result = await NewProvider(local, remote, knowledge)
            .SyncAsync(new SyncOptions { Direction = SyncDirection.Upload, Scope = Scope });

        result.Created.Should().Be(1);
        (await remote.ReadAsync(localItem.Guid!.Value)).Should().NotBeNull();
    }

    [Fact]
    public async Task SyncAsync_Download_RemoteDeletedTombstone_DeletesLocalCopy()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await knowledge.SetLastSyncTimeAsync(Scope, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var guid = Guid.NewGuid();
        await local.CreateAsync(new TestSyncModel { Guid = guid, Name = "Local survivor" });
        await knowledge.CreateAsync(new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = Scope,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
            IsRemoteDeleted = true
        });

        var result = await NewProvider(local, remote, knowledge)
            .SyncAsync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        result.Deleted.Should().Be(1);
        (await local.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsCreateCounts_WithoutMutating()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "A" });
        await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "B" });

        var preview = await NewProvider(local, remote, knowledge)
            .PreviewAsync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        preview.ToCreate.Should().Be(2);
        preview.Items.Should().HaveCount(2);
        (await local.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_UpdatesLastSyncTime()
    {
        var local = new TestAsyncBulkStore();
        var remote = new TestAsyncBulkStore();
        var knowledge = new TestAsyncSyncKnowledgeItemStore();
        await remote.CreateAsync(new TestSyncModel { Guid = Guid.NewGuid(), Name = "Item" });

        await NewProvider(local, remote, knowledge)
            .SyncAsync(new SyncOptions { Direction = SyncDirection.Download, Scope = Scope });

        (await knowledge.GetLastSyncTimeAsync(Scope, CancellationToken.None)).Should().NotBeNull();
    }
}
