using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.View.Tests;

/// <summary>
/// CR-L194: the SQLite view overrides had no test project. Covers the ViewExists argument guard, the
/// CREATE VIEW IF NOT EXISTS DDL string, and a real round-trip (present vs absent view) against an on-disk
/// SQLite database. CR-L193: CreateView is non-replacing on SQLite (documented on BuildCreateViewSql).
/// </summary>
public class SqLiteViewTests : IDisposable
{
    private readonly string _root;

    public SqLiteViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private SqLiteConnector NewConnector()
        => (SqLiteConnector)new SqLiteStoreFactory(
            new SqLiteStoreFactoryOptions { Location = _root, Name = "views.db" }).GetConnector();

    private string ConnectionString() => new SqLiteSettings(_root, "views.db").GetConnectionString();

    private void Seed(string sql)
    {
        using var conn = new SqliteConnection(ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ViewExists_NullOrWhitespace_Throws(string? viewName)
    {
        NewConnector().Invoking(c => c.ViewExists(viewName!)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ViewExistsAsync_NullOrWhitespace_Throws(string? viewName)
    {
        await NewConnector().Invoking(c => c.ViewExistsAsync(viewName!)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void BuildCreateViewSql_EmitsIfNotExists()
    {
        var connector = NewConnector();
        var method = typeof(SqLiteConnector).GetMethod(
            "BuildCreateViewSql", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var sql = (string)method!.Invoke(connector, new object[] { "MyView", "SELECT 1 AS X" })!;

        sql.Should().Be("CREATE VIEW IF NOT EXISTS \"MyView\" AS SELECT 1 AS X");
    }

    [Fact]
    public void ViewExists_TrueForRealView_FalseForMissing()
    {
        Seed("CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT); " +
             "CREATE VIEW WidgetNames AS SELECT Name FROM Widgets;");
        var connector = NewConnector();

        connector.ViewExists("WidgetNames").Should().BeTrue();
        connector.ViewExists("NoSuchView").Should().BeFalse();
        // A table of the same shape is NOT a view — the type='view' filter must exclude it.
        connector.ViewExists("Widgets").Should().BeFalse();
    }

    [Fact]
    public async Task ViewExistsAsync_TrueForRealView_FalseForMissing()
    {
        Seed("CREATE VIEW OnlyAsync AS SELECT 1 AS X;");
        var connector = NewConnector();

        (await connector.ViewExistsAsync("OnlyAsync")).Should().BeTrue();
        (await connector.ViewExistsAsync("Missing")).Should().BeFalse();
    }
}
