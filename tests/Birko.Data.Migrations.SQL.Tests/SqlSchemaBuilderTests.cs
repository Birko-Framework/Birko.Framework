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
    public void CollectionExists_WorksOnSqlite_WithoutInformationSchema()
    {
        // CR-L152: CollectionExists used to query INFORMATION_SCHEMA unconditionally, which SQLite does
        // not provide (it uses sqlite_master) — so this threw on SQLite. It now picks the catalog query
        // from the connection's provider.
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        schema.CollectionExists("Users").Should().BeFalse("no table yet");

        schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .Build();

        schema.CollectionExists("Users").Should().BeTrue();
        schema.CollectionExists("Missing").Should().BeFalse();
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

    /// <summary>
    /// TASK-249 — a migration's index column name is <b>caller text</b> that reaches interpolated DDL, so it
    /// is refused unless it is a plain unqualified identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-245 made index columns be emitted <b>bare</b> in <c>AbstractConnectorBase.CreateIndexSql</c> —
    /// required, because a quoted column identifier cannot resolve the case-folded column that bare-column
    /// <c>CREATE TABLE</c> actually creates on PostgreSQL. That removed an <i>accidental</i> containment:
    /// <c>QuoteIdentifier</c> had been neutralising a hostile name here, and this builder's connector path
    /// (<c>Build()</c> → <c>Tables.IndexColumn.ColumnName</c> → <c>_connector.CreateIndexes</c>) puts the
    /// caller's text into the statement verbatim. It never passes through
    /// <c>SqlIndexManager.ToSqlIndexDefinition</c>, so the guard added there for the sibling sink did not
    /// cover this one — the fix's own rule ("enumerate that sink's callers by provenance") applied to only
    /// one of two callers.
    /// </para>
    /// <para>
    /// Validated in <c>WithField</c> rather than <c>Build()</c> so it fails at the declaration site and
    /// covers both of <c>Build()</c>'s routes, and through the same
    /// <c>DataBase.ValidateIndexFieldIdentifier</c> the index manager uses, so the two sinks cannot drift.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Rank); CREATE TABLE Pwned (x INTEGER); --")]
    [InlineData("Name, (SELECT 1)")]
    [InlineData("Users.Name")]
    [InlineData("Name Rank")]
    public void CreateIndex_WithField_RefusesANameThatIsNotAPlainIdentifier(string fieldName)
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        var act = () => schema.CreateIndex("Users", "IX_Users_Bad").WithField(fieldName);

        act.Should().Throw<System.ArgumentException>(
            "index columns are interpolated bare into CREATE INDEX, so a migration must not be able to "
          + "append a second statement through a column name");
    }

    /// <summary>The guard must not refuse the legitimate case — and the test above would pass if it did.</summary>
    [Fact]
    public void CreateIndex_WithField_AcceptsAPlainColumnName()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, null);

        schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 100)
            .Build();

        schema.CreateIndex("Users", "IX_Users_Ok").WithField("Name").Build();

        CountMaster(conn, "index", "IX_Users_Ok").Should().Be(1);
    }
}
