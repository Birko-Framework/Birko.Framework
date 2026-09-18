using System;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Sql.Models;
using Birko.Data.Sync.Sql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Sql.Tests;

/// <summary>
/// CR-M166: covers the CRUD-free <see cref="AsyncSqlSyncKnowledgeStore{DB}.CreateKnowledgeItem"/>
/// deletion-flag derivation. The GetLastSyncTime/SetLastSyncTime CRUD paths — which used to be blocked
/// on SQLite by a connector DDL bug (invalid <c>AUTOINCREMENT</c> on the dual-key model's non-PK
/// <c>[IncrementField] Id</c>) — now run against a real SQLite database in
/// <see cref="AsyncSqlSyncKnowledgeStoreCrudTests"/> after that bug was fixed under TASK-058.
/// </summary>
public class AsyncSqlSyncKnowledgeStoreTests
{
    private static AsyncSqlSyncKnowledgeStore<SqLiteConnector> NewStore()
    {
        // CreateKnowledgeItem touches no database, so settings need not point at a real file.
        var store = new AsyncSqlSyncKnowledgeStore<SqLiteConnector>();
        store.SetSettings(new SqLiteSettings(Path.GetTempPath(), "unused-syncsql.db"));
        return store;
    }

    [Fact]
    public void CreateKnowledgeItem_CopiesIdentityAndScope()
    {
        var store = NewStore();
        var guid = Guid.NewGuid();

        var item = store.CreateKnowledgeItem(guid, "lh", "rh", new SyncOptions { Scope = "Products" });

        item.EntityGuid.Should().Be(guid);
        item.Scope.Should().Be("Products");
        item.Guid.Should().NotBeNull();
        item.LocalVersion.Should().Be("lh");
        item.RemoteVersion.Should().Be("rh");
    }

    [Fact]
    public void CreateKnowledgeItem_DerivesDeletionFlagsFromHashes()
    {
        var store = NewStore();
        var guid = Guid.NewGuid();
        var options = new SyncOptions { Scope = "S" };

        var both = store.CreateKnowledgeItem(guid, "lh", "rh", options);
        both.IsLocalDeleted.Should().BeFalse();
        both.IsRemoteDeleted.Should().BeFalse();

        store.CreateKnowledgeItem(guid, "", "rh", options).IsLocalDeleted.Should().BeTrue("an empty local hash means the item is gone locally");
        store.CreateKnowledgeItem(guid, null, "rh", options).IsLocalDeleted.Should().BeTrue();
        store.CreateKnowledgeItem(guid, "lh", "", options).IsRemoteDeleted.Should().BeTrue();
        store.CreateKnowledgeItem(guid, "lh", null, options).IsRemoteDeleted.Should().BeTrue();
    }
}
