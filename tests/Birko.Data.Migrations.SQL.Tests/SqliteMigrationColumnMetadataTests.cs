using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// <b>TASK-264, the live half.</b> The offline suite
/// (<see cref="MigrationColumnMetadataTests"/>) proves what <c>FieldDefinition</c> emits; this proves the
/// column that actually lands in the database when a real migration runs through
/// <c>SqlSchemaBuilder</c> — including the one thing no DDL-string assertion can show, that
/// <c>IsIgnored</c> now suppresses the column entirely.
///
/// <para>
/// The database is file-backed rather than <c>:memory:</c> so the connector and the externally supplied
/// connection address the same store, and the connector is real — TASK-247 removed the
/// <c>connector == null</c> fallback that made six tests in this project exercise a branch nothing ships,
/// which is criterion 4 of this task.
/// </para>
/// </summary>
public class SqliteMigrationColumnMetadataTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SqliteMigrationColumnMetadataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-task264-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "columns.db");
    }

    // Deliberately no SqliteConnection.ClearAllPools() here. It is process-wide, and TASK-276 measured
    // per-class teardowns calling it taking the SQL suite from clean to 1-2 failures per 6 runs.
    public void Dispose()
    {
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

    /// <summary>Reads the declared column type straight out of SQLite's own catalogue.</summary>
    private static Dictionary<string, string> Columns(DbConnection conn, string table)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(1)] = reader.GetString(2);
        }
        return result;
    }

    /// <summary>
    /// ⚠ <b>The one that matters most, and it is on the default provider.</b> A migration declaring
    /// <c>precision: 18, scale: 2</c> used to get a <c>REAL</c> column — binary floating point for money —
    /// because <c>SchemaField</c> was not a <c>DecimalField</c> and SQLite's <c>ConvertType</c> falls back
    /// to <c>REAL</c> for an unqualified decimal. It now lands as <c>NUMERIC(18,2)</c>.
    /// </summary>
    [Fact]
    public void A_declared_scale_lands_as_NUMERIC_not_REAL()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Invoices")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Total", FieldType.Decimal, precision: 18, scale: 2)
            .Build();

        Columns(conn, "Invoices")["Total"].Should().Be("NUMERIC(18,2)",
            "a REAL column cannot hold 18.2 decimal money exactly, and the declaration asked for it");
    }

    /// <summary>
    /// The string half, end to end. SQLite stores <c>TEXT</c> either way — it has no length-enforcing
    /// string type — so this asserts the column exists with the type SQLite actually uses rather than
    /// pretending a length is enforced here. The providers where the length is load-bearing are asserted
    /// in the offline suite.
    /// </summary>
    [Fact]
    public void A_declared_maxLength_still_produces_a_TEXT_column_on_sqlite()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("People")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 50)
            .Build();

        Columns(conn, "People")["Name"].Should().Be("TEXT");
    }

    /// <summary>
    /// <see cref="FieldDescriptor.IsIgnored"/> was never read, so a descriptor marked ignored got a column
    /// anyway. It is now honoured where "which fields become columns" is decided, matching the
    /// <c>[IgnoreField]</c> / <c>[NotMapped]</c> check <c>CreateAbstractField</c> performs before its own
    /// dispatch — so "not a column" means the same thing on both paths.
    /// </summary>
    [Fact]
    public void An_ignored_descriptor_gets_no_column()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Partial")
            .WithField(new FieldDescriptor { Name = "Id", Type = FieldType.Guid, IsPrimary = true })
            .WithField(new FieldDescriptor { Name = "Kept", Type = FieldType.String, MaxLength = 10 })
            .WithField(new FieldDescriptor { Name = "Skipped", Type = FieldType.String, IsIgnored = true })
            .Build();

        var columns = Columns(conn, "Partial");
        columns.Should().ContainKey("Kept");
        columns.Should().NotContainKey("Skipped",
            "IsIgnored means the descriptor describes no column, exactly as [IgnoreField] does");
    }

    /// <summary>
    /// An ignored field is dropped without disturbing the table around it — a filter applied to the wrong
    /// collection, or one that short-circuited the whole projection, would look identical on the test
    /// above.
    /// </summary>
    [Fact]
    public void Ignoring_one_field_does_not_disturb_the_others()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Ordered")
            .WithField(new FieldDescriptor { Name = "A", Type = FieldType.Integer })
            .WithField(new FieldDescriptor { Name = "Skipped", Type = FieldType.String, IsIgnored = true })
            .WithField(new FieldDescriptor { Name = "B", Type = FieldType.Integer })
            .Build();

        Columns(conn, "Ordered").Keys.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    /// <summary>
    /// A declared <c>ColumnName</c> is what the table actually gets. Offline this is a string assertion;
    /// here it is the catalogue, which is the only thing that settles whether the column is addressable.
    /// </summary>
    [Fact]
    public void A_declared_ColumnName_is_the_column_in_the_catalogue()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Renamed")
            .WithField(new FieldDescriptor
            {
                Name = "LogicalName",
                ColumnName = "physical_name",
                Type = FieldType.String,
                MaxLength = 10
            })
            .Build();

        var columns = Columns(conn, "Renamed");
        columns.Should().ContainKey("physical_name");
        columns.Should().NotContainKey("LogicalName");
    }

    /// <summary>
    /// <c>AddField</c> is the second of the three construction sites, and it reaches the connector by a
    /// different method (<c>AlterTableAdd</c>). Without this the factory could have been wired into
    /// <c>CreateCollection</c> alone and the ALTER path would still drop everything.
    /// </summary>
    [Fact]
    public void AddField_carries_the_declared_metadata_too()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("Growing")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .Build();

        schema.AddField("Growing", new FieldDescriptor
        {
            Name = "Amount",
            Type = FieldType.Decimal,
            Precision = 12,
            Scale = 4
        });

        Columns(conn, "Growing")["Amount"].Should().Be("NUMERIC(12,4)",
            "the ALTER TABLE ADD path goes through the same factory as CREATE TABLE");
    }
}
