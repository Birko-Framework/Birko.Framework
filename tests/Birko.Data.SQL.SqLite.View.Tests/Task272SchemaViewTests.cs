using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.View.Tests;

[Table("T272LiteEvts", Schema = "t272lite")]
public class T272LiteEvt : AbstractModel
{
    public string? Name { get; set; }
    public int Qty { get; set; }
}

[Table("T272LitePlain")]
public class T272LitePlain : AbstractModel
{
    public string? Name { get; set; }
}

[View(typeof(T272LiteEvt), typeof(T272LitePlain), nameof(T272LiteEvt.Name), nameof(T272LitePlain.Name))]
public class T272LiteView
{
    [ViewField(typeof(T272LiteEvt), nameof(T272LiteEvt.Qty))]
    public int Qty { get; set; }

    [ViewField(typeof(T272LitePlain), nameof(T272LitePlain.Name))]
    public string? Name { get; set; }
}

/// <summary>
/// TASK-272, live on SQLite: a persistent view over a table declared in a schema. SQLite stores it as
/// <c>t272lite__T272LiteEvts</c> while the view's qualifiers name <c>T272LiteEvts</c>, so its FROM must alias the table.
/// </summary>
public class Task272SchemaViewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t272-view-{Guid.NewGuid():N}");

    public Task272SchemaViewTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(new SqLiteSettings(_root, "views.db").GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void A_persistent_view_reads_a_table_stored_under_its_schema_prefix()
    {
        var connector = DataBase.GetConnector<SqLiteConnector>(new SqLiteSettings(_root, "views.db"));
        connector.CreateTable(new[] { typeof(T272LiteEvt), typeof(T272LitePlain) });
        connector.Insert(typeof(T272LiteEvt), new T272LiteEvt { Guid = Guid.NewGuid(), Name = "a", Qty = 1 });
        connector.Insert(typeof(T272LiteEvt), new T272LiteEvt { Guid = Guid.NewGuid(), Name = "b", Qty = 2 });
        connector.Insert(typeof(T272LitePlain), new T272LitePlain { Guid = Guid.NewGuid(), Name = "a" });

        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='t272lite__T272LiteEvts'").Should().Be(1);

        connector.CreateView(typeof(T272LiteView), "t272_lite_view");

        Scalar("SELECT COUNT(*) FROM \"t272_lite_view\"").Should().Be(1);
    }
}
