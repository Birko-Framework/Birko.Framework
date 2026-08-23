using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-275 — <c>[UniqueField]</c> on a nullable column, on a provider that was never broken.
/// </summary>
/// <remarks>
/// <para>
/// The fix moves the constraint from an inline <c>UNIQUE</c> to a partial unique index, because the inline
/// form admits only one NULL row on SQL Server. SQLite treats NULLs as distinct, so <b>the observable rule
/// here is identical either way</b> — which is exactly what this suite is for: the DDL changed on this
/// provider, so the behaviour has to be shown not to.
/// </para>
/// <para>
/// It also pins the boundary of the change: a <c>[RequiredField]</c> unique column keeps its inline
/// constraint, so a fix aimed at nullable columns cannot quietly restructure every table.
/// </para>
/// </remarks>
public class NullableUniqueColumnEndToEndTests : IDisposable
{
    private readonly string _root;
    private static int _seq;

    public NullableUniqueColumnEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-nullunique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("SqNullableUnique")]
    public class NullableRow : AbstractLogModel
    {
        [UniqueField]
        public string? Code { get; set; }
    }

    [Table("SqRequiredUnique")]
    public class RequiredRow : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public string Code { get; set; } = null!;
    }

    private SqLiteSettings NewDatabase() => new(_root, $"nu{Interlocked.Increment(ref _seq)}.db");

    private static string TableSql(SqLiteSettings settings, string table)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @t";
        command.Parameters.AddWithValue("@t", table);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static string IndexSql(SqLiteSettings settings, string index)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @i";
        command.Parameters.AddWithValue("@i", index);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static int? Insert(SqLiteSettings settings, string table, string? code)
    {
        try
        {
            using var connection = new SqliteConnection(settings.GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO {table} (Guid, CreatedAt, UpdatedAt, Code) VALUES (@g, @c, @c, @v)";
            command.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@c", DateTime.UtcNow);
            command.Parameters.AddWithValue("@v", (object?)code ?? DBNull.Value);
            command.ExecuteNonQuery();
            return null;
        }
        catch (SqliteException ex)
        {
            return ex.SqliteExtendedErrorCode;
        }
    }

    [Fact]
    public void A_nullable_unique_column_moves_to_a_partial_index()
    {
        var settings = NewDatabase();
        var connector = new SqLiteConnector(settings);
        connector.CreateTable(new[] { typeof(NullableRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        TableSql(settings, "SqNullableUnique").Should().NotContain("UNIQUE",
            "the inline constraint is gone — it is the shape that cannot admit a second NULL on MSSql");
        IndexSql(settings, "ux_SqNullableUnique_Code")
            .Should().Contain("UNIQUE").And.Contain("WHERE Code IS NOT NULL");
    }

    /// <summary>
    /// The behaviour is what it always was here: many NULLs, no duplicate values. 2067 is
    /// <c>SQLITE_CONSTRAINT_UNIQUE</c>.
    /// </summary>
    [Fact]
    public void The_rule_is_unchanged_on_a_provider_that_treats_nulls_as_distinct()
    {
        var settings = NewDatabase();
        new SqLiteConnector(settings).CreateTable(new[] { typeof(NullableRow) });

        Insert(settings, "SqNullableUnique", null).Should().BeNull();
        Insert(settings, "SqNullableUnique", null).Should().BeNull("SQLite always admitted many NULLs");
        Insert(settings, "SqNullableUnique", "C-1").Should().BeNull();
        Insert(settings, "SqNullableUnique", "C-1").Should().Be(2067);
    }

    /// <summary>
    /// The boundary of the change: a required unique column keeps the inline constraint and gains no index.
    /// </summary>
    [Fact]
    public void A_required_unique_column_keeps_its_inline_constraint()
    {
        var settings = NewDatabase();
        new SqLiteConnector(settings).CreateTable(new[] { typeof(RequiredRow) });

        TableSql(settings, "SqRequiredUnique").Should().Contain("UNIQUE");
        IndexSql(settings, "ux_SqRequiredUnique_Code").Should().BeEmpty("nothing was synthesised");

        Insert(settings, "SqRequiredUnique", "C-1").Should().BeNull();
        Insert(settings, "SqRequiredUnique", "C-1").Should().Be(2067);
    }

    /// <summary>
    /// The producer of the decision, both sides — so "nullable moves, required stays" is asserted once at
    /// the level it is decided rather than only through emitted DDL.
    /// </summary>
    [Fact]
    public void UsesInlineUniqueConstraint_is_false_only_for_a_nullable_unique_column()
    {
        var nullable = Birko.Data.SQL.DataBase.LoadTable(typeof(NullableRow));
        var required = Birko.Data.SQL.DataBase.LoadTable(typeof(RequiredRow));

        nullable.Fields.Values.Single(f => f.Name == "Code").UsesInlineUniqueConstraint.Should().BeFalse();
        required.Fields.Values.Single(f => f.Name == "Code").UsesInlineUniqueConstraint.Should().BeTrue();

        nullable.Fields.Values.Single(f => f.Name == "Code").IsInIndexKey.Should().BeTrue(
            "TASK-257: the column is still an index key whichever shape carries the uniqueness, so a "
          + "provider with restricted key types must still bound it");
    }
}
