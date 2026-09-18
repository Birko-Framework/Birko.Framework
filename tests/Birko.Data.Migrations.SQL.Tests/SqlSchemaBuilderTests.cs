using System;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
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
/// no-op default for eager providers) that SQL overrides.
///
/// <para>
/// <b>These tests used to pass <c>connector: null</c>, and that was the problem</b> (TASK-247). A null
/// connector selected a hand-written raw-SQL fallback in every method, so this whole file exercised a branch
/// no production migration ever takes — which is exactly how TASK-246's missing <c>Unique</c> flag on the
/// <i>live</i> branch stayed green while six tests passed. The fallbacks are now deleted and the connector is
/// required, so these assertions are about the real path.
/// </para>
/// <para>
/// The database is file-backed rather than <c>:memory:</c> so the connector and the externally supplied
/// connection address the same store.
/// </para>
/// </summary>
public class SqlSchemaBuilderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SqlSchemaBuilderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-schemabuilder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "schema.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AbstractConnector Connector()
        => DataBase.GetConnector<SqLiteConnector>(new SqLiteSettings(_dir, Path.GetFileName(_dbPath)));

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

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
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Users")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 100)
            .Build();

        schema.CreateIndex("Users", "IX_Users_Ok").WithField("Name").Build();

        CountMaster(conn, "index", "IX_Users_Ok").Should().Be(1);
    }

    /// <summary>
    /// <b>TASK-295 — a table created through the migration builder is recorded on the connector, and that
    /// is the reason the recording sits where it does.</b>
    ///
    /// <para>The defect was that <c>RecordTableCreated</c> was called from the <b>virtual</b>
    /// <c>AbstractConnector.CreateTable(string, IEnumerable&lt;string&gt;)</c> — the very method every
    /// provider overrides — so <c>TablesCreated</c> was permanently empty on four of five connectors. The
    /// obvious alternative fix was to record in the <c>IDictionary</c> dispatcher instead, which every
    /// override funnels through.</para>
    ///
    /// <para>⚠ <b>This test is what rules that alternative out.</b> <c>SqlSchemaBuilder</c> is the one
    /// external caller that reaches the single-table overload <b>directly</b>
    /// (<c>SqlSchemaBuilder.cs</c>, <c>Build()</c>), bypassing the dispatcher entirely — so recording
    /// there would have left every migration-created table unrecorded, silently, and only on this path.
    /// The chosen shape is a non-virtual public wrapper around a <c>protected virtual CreateTableCore</c>,
    /// which covers this caller and the four provider overrides at once.</para>
    /// </summary>
    [Fact]
    public void TASK295_a_table_created_through_the_builder_is_recorded_on_the_connector()
    {
        using var conn = OpenConnection();
        var connector = Connector();
        var schema = new SqlSchemaBuilder(conn, null, connector);

        schema.CreateCollection("BuilderRecorded")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 100)
            .Build();

        CountMaster(conn, "table", "BuilderRecorded").Should().Be(1, "the premise: the table was created");
        connector.TablesCreated.Keys.Should().Contain("BuilderRecorded",
            "the migration builder calls the single-table overload directly, so a recording placed in the "
            + "IDictionary dispatcher would miss it — which is why it is in the wrapper instead");
    }
}
