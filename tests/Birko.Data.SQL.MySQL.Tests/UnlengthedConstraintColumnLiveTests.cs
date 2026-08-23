using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-265 — an unlengthed string carrying an <b>inline</b> constraint on MySQL.
/// </summary>
/// <remarks>
/// <para>
/// MySQL cannot use a BLOB/TEXT column in a key without a key length, so <c>LONGTEXT UNIQUE</c> and
/// <c>LONGTEXT PRIMARY KEY</c> are both <c>ERROR 1170</c> at <c>CREATE TABLE</c> — measured on 8.4.11, which
/// means such an entity's table could not be created here at all. <c>ConvertType</c> now reads
/// <c>AbstractField.IsInIndexKey</c> rather than the narrower <c>IsIndexed</c>, so the column is bounded to
/// <c>VARCHAR(255)</c> and both constraints become expressible.
/// </para>
/// <para>
/// <b>Scope, after TASK-275.</b> That task moved every <i>nullable</i> <c>[UniqueField]</c> column onto a
/// synthesised index, which set <c>IsIndexed</c> and bounded it through the older branch. What remained —
/// and what this suite covers — is the shapes that keep an inline constraint: a <c>[RequiredField]</c>
/// unique column and a <c>[PrimaryField]</c> one.
/// </para>
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class UnlengthedConstraintColumnLiveTests : IDisposable
{
    private const string UniqueTable = "MyUnlengthedUnique";
    private const string PrimaryTable = "MyUnlengthedPrimary";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public UnlengthedConstraintColumnLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    /// <summary>Unlengthed and <c>[RequiredField]</c>, so the UNIQUE stays inline (TASK-275).</summary>
    [Table(UniqueTable)]
    public class UniqueRow : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public string Code { get; set; } = null!;
    }

    /// <summary>An unlengthed string primary key — <c>PRIMARY KEY</c> is always inline.</summary>
    [Table(PrimaryTable)]
    public class PrimaryRow
    {
        [PrimaryField]
        public string NaturalKey { get; set; } = null!;

        public string? Label { get; set; }
    }

    private static void Exec(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ColumnType(string table, string column)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_TYPE FROM information_schema.columns WHERE table_schema = DATABASE() "
                            + "AND table_name = @t AND column_name = @c";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@c", column);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    /// <summary>The constraint itself, from the catalogue — not "CreateTable did not throw".</summary>
    private static string ConstraintKind(string table, string column)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.INDEX_NAME FROM information_schema.statistics s "
                            + "WHERE s.table_schema = DATABASE() AND s.table_name = @t AND s.column_name = @c "
                            + "AND s.NON_UNIQUE = 0";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@c", column);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static int? Insert(string table, string columns, string values)
    {
        try
        {
            using var connection = new MySqlConnection(Settings().GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO `{table}` ({columns}) VALUES ({values})";
            command.ExecuteNonQuery();
            return null;
        }
        catch (MySqlException ex)
        {
            return (int)ex.Number;
        }
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS `{UniqueTable}`"); } catch { }
        try { Exec($"DROP TABLE IF EXISTS `{PrimaryTable}`"); } catch { }
    }

    /// <summary>
    /// A required unique column: the table is creatable at all now, and the constraint is real.
    /// </summary>
    [Fact]
    public void An_unlengthed_required_unique_column_is_bounded_and_enforced()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{UniqueTable}`");

        new MySQLConnector(Settings()).CreateTable(new[] { typeof(UniqueRow) });

        ColumnType(UniqueTable, "Code").Should().Be("varchar(255)",
            "LONGTEXT UNIQUE is ERROR 1170 — the table could not be created at all before");
        ConstraintKind(UniqueTable, "Code").Should().NotBeEmpty("a genuine UNIQUE index must exist");

        Insert(UniqueTable, "`Guid`, `CreatedAt`, `UpdatedAt`, `Code`",
               "UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), 'C-1'").Should().BeNull();
        Insert(UniqueTable, "`Guid`, `CreatedAt`, `UpdatedAt`, `Code`",
               "UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), 'C-1'").Should().Be(1062,
               "and it is enforced, not merely present");
    }

    /// <summary>An unlengthed string primary key — the other shape that kept the inline form.</summary>
    [Fact]
    public void An_unlengthed_string_primary_key_is_bounded_and_enforced()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{PrimaryTable}`");

        new MySQLConnector(Settings()).CreateTable(new[] { typeof(PrimaryRow) });

        ColumnType(PrimaryTable, "NaturalKey").Should().Be("varchar(255)");
        ConstraintKind(PrimaryTable, "NaturalKey").Should().Be("PRIMARY");

        Insert(PrimaryTable, "`NaturalKey`, `Label`", "'K-1', 'first'").Should().BeNull();
        Insert(PrimaryTable, "`NaturalKey`, `Label`", "'K-1', 'second'").Should().Be(1062);
    }

    /// <summary>
    /// ⚠ The reason bounding is acceptable at all: the over-long write is <b>refused</b>, not silently
    /// truncated. That rests on <c>sql_mode</c> carrying <c>STRICT_TRANS_TABLES</c> (the 8.x default, which
    /// this framework never changes) — without it MySQL truncates with a warning, and a truncated value
    /// would make the UNIQUE constraint quietly weaker than declared, which is exactly the outcome TASK-248
    /// rejected prefix indexes to avoid.
    /// </summary>
    [Fact]
    public void An_over_long_write_is_refused_rather_than_truncated()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{UniqueTable}`");
        new MySQLConnector(Settings()).CreateTable(new[] { typeof(UniqueRow) });

        var tooLong = new string('x', 300);
        Insert(UniqueTable, "`Guid`, `CreatedAt`, `UpdatedAt`, `Code`",
               $"UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), '{tooLong}'")
            .Should().Be(1406, "Data too long — the value is rejected, so nothing is stored under a truncation");

        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM `{UniqueTable}`";
        Convert.ToInt32(count.ExecuteScalar()).Should().Be(0);
    }
}
