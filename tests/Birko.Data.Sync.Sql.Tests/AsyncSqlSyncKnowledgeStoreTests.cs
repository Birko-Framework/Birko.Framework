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
/// CR-M166 (partial): first test project for Birko.Data.Sync.Sql. Covers the CRUD-free
/// <see cref="AsyncSqlSyncKnowledgeStore{DB}.CreateKnowledgeItem"/> deletion-flag derivation.
///
/// The store's GetLastSyncTime/SetLastSyncTime CRUD paths can NOT run against the SQLite connector:
/// <see cref="SqlSyncKnowledgeItem"/> carries a non-primary <c>[IncrementField] Id</c> alongside the
/// <c>[PrimaryField] Guid</c>, and <c>SqLiteConnector.CreateTable</c> emits
/// <c>Id INTEGER NOT NULL AUTOINCREMENT</c>, which is a SQLite syntax error (SQLite only allows
/// AUTOINCREMENT on <c>INTEGER PRIMARY KEY</c>, and a table has one primary key). That is a
/// SqLiteConnector DDL limitation for dual-key models — exercising the CRUD methods needs a real
/// MSSql/PostgreSQL backend (Docker), so it's tracked there rather than forced onto SQLite.
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
