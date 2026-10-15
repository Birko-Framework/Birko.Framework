using System;
using System.IO;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Sync.Sql.Models;
using Birko.Data.Sync.Sql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Sql.Tests;

/// <summary>
/// SH-M067 / SH-M068 (TASK-157) on the synchronous SQL knowledge store, on a real SQLite database: the stamp reads the
/// scope fully before writing (the read was the connector's lazy reader, open while each row was updated), and reports
/// a stamp only when a row carried it.
/// </summary>
public class SqlSyncKnowledgeStoreStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-syncsql-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private SqlSyncKnowledgeStore<SqLiteConnector> NewStore()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "sync.db" });
        ((SqLiteConnector)factory.GetConnector()).CreateTable(new[] { typeof(SqlSyncKnowledgeItem) });
        var store = new SqlSyncKnowledgeStore<SqLiteConnector>();
        store.SetSettings(new SqLiteSettings(_root, "sync.db"));
        return store;
    }

    [Fact]
    public void Stamping_a_scope_updates_every_row()
    {
        var store = NewStore();
        for (var i = 0; i < 5; i++)
            store.Create(new SqlSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "S", LastSyncedAt = DateTime.UtcNow.AddDays(-i - 1) });
        var stamp = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        var returned = store.SetLastSyncTime("S", stamp);

        returned.Should().Be(stamp);
        store.Read(x => x.Scope == "S", null, null, null).Select(x => x.LastSyncedAt).Should().AllBeEquivalentTo(stamp);
    }

    [Fact]
    public void Stamping_an_empty_scope_reports_nothing_persisted()
    {
        var store = NewStore();

        store.SetLastSyncTime("empty", DateTime.UtcNow).Should().BeNull();
    }
}
