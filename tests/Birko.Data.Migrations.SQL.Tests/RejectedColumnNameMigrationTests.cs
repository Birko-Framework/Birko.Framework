using System;
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
/// TASK-862 — a migration's own column named with a word the provider rejects is refused by name, before the DDL is
/// sent, on all three of the schema builder's column sinks: <c>CreateCollection(..).Build()</c> (which hands the
/// connector rendered definitions, so it checks first), <c>AddField</c> (through <c>AlterTableAdd</c>) and
/// <c>RenameField</c>. Before, each failed with SQLite's <c>near "Order": syntax error</c>.
/// </summary>
public sealed class RejectedColumnNameMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"birko-t862-migrations-{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public RejectedColumnNameMigrationTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "schema.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_dir);
    }

    private AbstractConnector Connector()
        => DataBase.GetConnector<Birko.Data.SQL.Connectors.SqLiteConnector>(new SqLiteSettings(_dir, Path.GetFileName(_dbPath)));

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    [Fact]
    public void CreateCollection_refuses_a_rejected_column_before_the_table_exists()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        var act = () => schema.CreateCollection("T862Migrated")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Order", FieldType.Integer)
            .Build();

        act.Should().Throw<NotSupportedException>().WithMessage("*\"Order\"*T862Migrated*reserved word on SQLite*");
        schema.CollectionExists("T862Migrated").Should().BeFalse("the refusal comes before CREATE TABLE");
    }

    [Fact]
    public void AddField_and_RenameField_refuse_a_rejected_name_and_leave_the_table_as_it_was()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());
        schema.CreateCollection("T862Kept")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Name", FieldType.String, maxLength: 50)
            .Build();

        FluentActions.Invoking(() => schema.AddField("T862Kept", new FieldDescriptor { Name = "Group", Type = FieldType.Integer }))
            .Should().Throw<NotSupportedException>().WithMessage("*\"Group\"*reserved word on SQLite*");
        FluentActions.Invoking(() => schema.RenameField("T862Kept", "Name", "Select"))
            .Should().Throw<NotSupportedException>().WithMessage("*\"Select\"*reserved word on SQLite*");

        using var command = conn.CreateCommand();
        command.CommandText = "SELECT group_concat(name) FROM pragma_table_info('T862Kept')";
        command.ExecuteScalar().Should().Be("Id,Name");
    }

    [Fact]
    public void A_keyword_SQLite_accepts_is_still_a_valid_migration_column()
    {
        using var conn = OpenConnection();
        var schema = new SqlSchemaBuilder(conn, null, Connector());

        schema.CreateCollection("T862Accepted")
            .WithField("Id", FieldType.Guid, isPrimary: true)
            .WithField("Key", FieldType.Integer)
            .Build();

        schema.CollectionExists("T862Accepted").Should().BeTrue();
    }
}
