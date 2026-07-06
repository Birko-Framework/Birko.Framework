using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// Regression tests for CR-C14: SqlSchemaBuilder.CreateCollection / CreateIndex returned fluent
/// builders whose Build() (which emits the CREATE TABLE / CREATE INDEX) was internal and never
/// invoked — the ICollectionBuilder / IIndexBuilder interfaces exposed no terminal, so table/index
/// creation via the fluent API was a silent no-op. Build() is now a terminal on the interfaces (a
/// no-op default for eager providers) that SQL overrides. Exercised against a real in-memory SQLite
/// connection via the raw-SQL fallback (no Birko connector required).
/// </summary>
public class SqlSchemaBuilderTests
{
    private static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static long CountMaster(DbConnection conn, string type, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='{type}' AND name=@n";
        var p = cmd.CreateParameter();
        p.ParameterName = "@n";
        p.Value = name;
        cmd.Parameters.Add(p);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void CreateCollection_Build_CreatesTheTable()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 100)
            .Build();

        CountMaster(conn, "table", "Users").Should().Be(1);
    }

    [Fact]
    public void CreateCollection_WithoutBuild_DoesNothing()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        // Chain without the terminal — nothing should be created (documents why Build() is required).
        schema.CreateCollection("Ghost")
            .WithField("Id", FieldType.Guid, isPrimary: true);

        CountMaster(conn, "table", "Ghost").Should().Be(0);
    }

    [Fact]
    public void CreateIndex_Build_CreatesTheIndex()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 100)
            .Build();

        schema.CreateIndex("Users", "IX_Users_Name")
            .WithField("Name")
            .Unique()
            .Build();

        CountMaster(conn, "index", "IX_Users_Name").Should().Be(1);
    }
}
