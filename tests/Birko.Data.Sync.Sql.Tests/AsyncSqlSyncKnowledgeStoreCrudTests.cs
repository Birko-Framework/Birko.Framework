using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Sql.Models;
using Birko.Data.Sync.Sql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Sql.Tests;

/// <summary>
/// CR-M166 CRUD tier, unblocked by TASK-058. The dual-key <see cref="SqlSyncKnowledgeItem"/>
/// (<c>[PrimaryField] Guid</c> + non-PK <c>[IncrementField] Id</c>) previously made
/// <c>SqLiteConnector.CreateTable</c> emit invalid <c>Id INTEGER NOT NULL AUTOINCREMENT</c> DDL and
/// throw. With the connector fixed (AUTOINCREMENT scoped to <c>INTEGER PRIMARY KEY</c> only; a non-PK
/// increment field is a plain column) the SQL sync store's CRUD paths now run against a real on-disk
/// SQLite database — no Docker required.
/// </summary>
public class AsyncSqlSyncKnowledgeStoreCrudTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-syncsql-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private AsyncSqlSyncKnowledgeStore<SqLiteConnector> NewStore()
    {
        // Create the schema up front against the same db file the store opens (mirrors the SqLite tier).
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "sync.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(SqlSyncKnowledgeItem) }); // TASK-058: valid DDL for the dual-key model

        var store = new AsyncSqlSyncKnowledgeStore<SqLiteConnector>();
        store.SetSettings(new SqLiteSettings(_root, "sync.db"));
        return store;
    }

    [Fact]
    public void CreateTable_DualKeyModel_ProducesValidDdl()
    {
        // The point of TASK-058: this no longer throws a SQLite syntax error.
        var act = () => NewStore();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetLastSyncTime_ReturnsMax_AndNullForUnknownScope()
    {
        var store = NewStore();
        var opts = new SyncOptions { Scope = "Products" };

        var older = store.CreateKnowledgeItem(Guid.NewGuid(), "l", "r", opts);
        older.LastSyncedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = store.CreateKnowledgeItem(Guid.NewGuid(), "l", "r", opts);
        newer.LastSyncedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(older);
        await store.CreateAsync(newer);

        (await store.GetLastSyncTimeAsync("Products", CancellationToken.None))
            .Should().BeCloseTo(newer.LastSyncedAt, TimeSpan.FromSeconds(1));

        (await store.GetLastSyncTimeAsync("Orders", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SetLastSyncTime_UpdatesEveryItemInScope()
    {
        var store = NewStore();
        var opts = new SyncOptions { Scope = "Products" };
        await store.CreateAsync(store.CreateKnowledgeItem(Guid.NewGuid(), "l", "r", opts));
        await store.CreateAsync(store.CreateKnowledgeItem(Guid.NewGuid(), "l", "r", opts));

        var stamp = new DateTime(2027, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        await store.SetLastSyncTimeAsync("Products", stamp, CancellationToken.None);

        var all = (await store.ReadAsync(x => x.Scope == "Products")).ToList();
        all.Should().HaveCount(2);
        all.Should().OnlyContain(x => Math.Abs((x.LastSyncedAt - stamp).TotalSeconds) < 1);
    }

    [Fact]
    public async Task SetLastSyncTime_NullTime_IsNoOp()
    {
        var store = NewStore();
        var opts = new SyncOptions { Scope = "Products" };
        var item = store.CreateKnowledgeItem(Guid.NewGuid(), "l", "r", opts);
        item.LastSyncedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        await store.CreateAsync(item);

        var result = await store.SetLastSyncTimeAsync("Products", null, CancellationToken.None);

        result.Should().BeNull();
        (await store.GetLastSyncTimeAsync("Products", CancellationToken.None))
            .Should().BeCloseTo(item.LastSyncedAt, TimeSpan.FromSeconds(1));
    }
}
