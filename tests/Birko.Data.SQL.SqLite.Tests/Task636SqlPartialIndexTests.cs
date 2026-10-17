using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-636: the portable predicates reach a SQL index as TASK-273's own, through <see cref="SqLiteIndexManager"/>, and
/// the index enforces them. A WhereNull predicate discriminates on SQLite: a unique index treats NULLs as distinct there, so a
/// WhereNotNull marker index behaves like a full one, while a deleted duplicate is accepted only by a partial index.
/// </summary>
public class Task636SqlPartialIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t636-{Guid.NewGuid():N}");

    private SqLiteConnector? _connector;

    public void Dispose()
    {
        // This test's own pool only (TASK-276): a process-wide clear disposes connections a parallel sibling is using.
        try
        {
            if (_connector?.CreateConnection(_connector.Settings) is Microsoft.Data.Sqlite.SqliteConnection own)
                using (own) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(own);
        }
        catch { }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class T636Currency : AbstractModel
    {
        public string? Code { get; set; }
        public bool? DefaultMarker { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private sealed class Mapping : IModelMapping<T636Currency>
    {
        public void Configure(ModelMap<T636Currency> map)
        {
            map.ToTable("T636Currencies").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Code).HasPrecision(10);
            map.Property(x => x.DefaultMarker);
            map.Property(x => x.DeletedAt);
        }
    }

    private bool Insert(SqLiteConnector connector, string code, string marker, string deletedAt = "NULL")
    {
        try
        {
            using var conn = connector.CreateConnection(connector.Settings);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO T636Currencies (Guid, Code, DefaultMarker, DeletedAt) VALUES ('{Guid.NewGuid()}', '{code}', {marker}, {deletedAt})";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
    }

    private SqLiteConnector Connector()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new Mapping());
        registry.ApplyToDatabase();
        var connector = (SqLiteConnector)new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "t636.db" }).GetConnector();
        connector.CreateTable(new[] { typeof(T636Currency) });
        _connector = connector;
        return connector;
    }

    [Fact]
    public async Task A_WhereNull_declaration_creates_a_unique_index_over_live_rows_only()
    {
        var connector = Connector();

        await new SqLiteIndexManager(connector).CreateAsync(new IndexDefinition
        {
            Name = "UX_T636_LiveCode",
            Fields = new[] { IndexField.Ascending("Code") },
            Unique = true,
            WhereNull = new[] { "DeletedAt" },
        }, "T636Currencies");

        Insert(connector, "EUR", "NULL").Should().BeTrue();
        Insert(connector, "EUR", "NULL", "'2026-10-17'").Should().BeTrue("a deleted row is not indexed");
        Insert(connector, "EUR", "NULL").Should().BeFalse("a second live EUR is refused");
    }

    [Fact]
    public async Task A_WhereNotNull_marker_index_keeps_one_default()
    {
        var connector = Connector();

        await new SqLiteIndexManager(connector).CreateAsync(new IndexDefinition
        {
            Name = "UX_T636_Default",
            Fields = new[] { IndexField.Ascending("DefaultMarker") },
            Unique = true,
            WhereNotNull = new[] { "DefaultMarker" },
        }, "T636Currencies");

        Insert(connector, "a", "NULL").Should().BeTrue();
        Insert(connector, "b", "NULL").Should().BeTrue();
        Insert(connector, "c", "1").Should().BeTrue();
        Insert(connector, "d", "1").Should().BeFalse("a second default is refused");
    }
}
