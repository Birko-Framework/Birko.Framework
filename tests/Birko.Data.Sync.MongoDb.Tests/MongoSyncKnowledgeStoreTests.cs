using System;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.MongoDb.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.MongoDb.Tests;

/// <summary>
/// CR-H102: the MongoDB sync store had no test project. CreateKnowledgeItem is pure (no I/O) and is
/// covered here; GetLastSyncTimeAsync/SetLastSyncTimeAsync route through the concrete Mongo store and
/// require a live MongoDB (documented integration gap).
/// </summary>
public class MongoSyncKnowledgeStoreTests
{
    private static AsyncMongoSyncKnowledgeStore NewStore() => new();

    [Fact]
    public void CreateKnowledgeItem_MapsScopeAndEntityAndVersions()
    {
        var store = NewStore();
        var entity = Guid.NewGuid();
        var options = new SyncOptions { Scope = "Products" };

        var item = store.CreateKnowledgeItem(entity, "localhash", "remotehash", options);

        item.EntityGuid.Should().Be(entity);
        item.Scope.Should().Be("Products");
        item.LocalVersion.Should().Be("localhash");
        item.RemoteVersion.Should().Be("remotehash");
        item.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeFalse();
    }

    [Fact]
    public void CreateKnowledgeItem_EmptyLocalHash_MarksLocalDeleted()
    {
        var store = NewStore();
        var item = store.CreateKnowledgeItem(Guid.NewGuid(), null, "remotehash", new SyncOptions { Scope = "S" });

        item.IsLocalDeleted.Should().BeTrue();
        item.IsRemoteDeleted.Should().BeFalse();
    }

    [Fact]
    public void CreateKnowledgeItem_EmptyRemoteHash_MarksRemoteDeleted()
    {
        var store = NewStore();
        var item = store.CreateKnowledgeItem(Guid.NewGuid(), "localhash", "", new SyncOptions { Scope = "S" });

        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeTrue();
    }
}
