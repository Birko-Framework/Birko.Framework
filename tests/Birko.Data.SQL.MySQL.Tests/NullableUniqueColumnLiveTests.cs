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
/// TASK-275 — <c>[UniqueField]</c> on a nullable column, on MySQL.
/// </summary>
/// <remarks>
/// <para>
/// MySQL treats NULLs as distinct, so it was never broken by the inline form and the observable rule is
/// unchanged. What makes this provider worth its own suite is the <i>shape</i>: the synthesised index
/// carries a <c>WhereNotNull</c> predicate, and MySQL supports no partial index — so TASK-273's policy
/// drops the term here, leaving a plain unique index that means the same thing. Both halves are asserted.
/// </para>
/// <para>
/// ⚠ <b>Overlap with TASK-265, measured rather than assumed.</b> An unlengthed <c>[UniqueField]</c> string is
/// <c>LONGTEXT</c> on MySQL and <c>LONGTEXT UNIQUE</c> is <c>ERROR 1170</c>, so such a table could not be
/// created here at all. Moving the constraint to an index makes the column an <c>IsIndexed</c> key, which
/// bounds it to <c>VARCHAR(255)</c> — so that shape now works. That is a side effect of this task, not its
/// purpose: TASK-265 still owns the <c>[PrimaryField]</c> and non-nullable <c>[UniqueField]</c> cases, which
/// keep the inline form and are untouched.
/// </para>
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class NullableUniqueColumnLiveTests : IDisposable
{
    private const string NullableTable = "MyNullableUnique";
    private const string UnlengthedTable = "MyUnlengthedNullableUnique";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public NullableUniqueColumnLiveTests(ITestOutputHelper output) => _output = output;

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

    [Table(NullableTable)]
    public class NullableRow : AbstractLogModel
    {
        [UniqueField]
        [MaxLengthField(64)]
        public string? Code { get; set; }
    }

    [Table(UnlengthedTable)]
    public class UnlengthedRow : AbstractLogModel
    {
        [UniqueField]
        public string? Code { get; set; }
    }

    private static void Exec(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int? Insert(string table, string? code)
    {
        try
        {
            using var connection = new MySqlConnection(Settings().GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO `{table}` (`Guid`, `CreatedAt`, `UpdatedAt`, `Code`) "
                                + "VALUES (UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), @c)";
            command.Parameters.AddWithValue("@c", (object?)code ?? DBNull.Value);
            command.ExecuteNonQuery();
            return null;
        }
        catch (MySqlException ex)
        {
            return (int)ex.Number;
        }
    }

    private static bool IndexExists(string table, string index)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() "
                            + "AND table_name = @t AND index_name = @i";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@i", index);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
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

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS `{NullableTable}`"); } catch { }
        try { Exec($"DROP TABLE IF EXISTS `{UnlengthedTable}`"); } catch { }
    }

    /// <summary>
    /// The index is built — the <c>IS NOT NULL</c> term is dropped here, since MySQL has no partial index
    /// and treats NULLs as distinct anyway (TASK-273's policy) — and the rule is what it always was.
    /// </summary>
    [Fact]
    public void A_nullable_unique_column_becomes_a_plain_unique_index_and_behaves_the_same()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{NullableTable}`");

        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(NullableRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        IndexExists(NullableTable, $"ux_{NullableTable}_Code").Should().BeTrue(
            "the predicate is dropped, so what MySQL gets is a legal plain unique index");

        Insert(NullableTable, null).Should().BeNull();
        Insert(NullableTable, null).Should().BeNull("MySQL always admitted many NULLs — unchanged");
        Insert(NullableTable, "C-1").Should().BeNull();
        Insert(NullableTable, "C-1").Should().Be(1062);
    }

    /// <summary>
    /// ⚠ The TASK-265 overlap, measured. <c>LONGTEXT UNIQUE</c> is <c>ERROR 1170</c> here, so this table
    /// could not be created at all before; the column is now an index key and therefore bounded.
    /// </summary>
    [Fact]
    public void An_unlengthed_nullable_unique_column_is_now_bounded_and_indexable()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{UnlengthedTable}`");

        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(UnlengthedRow) });

        ColumnType(UnlengthedTable, "Code").Should().StartWith("varchar",
            "moving the constraint to an index makes the column an index key, which MySQL bounds");
        IndexExists(UnlengthedTable, $"ux_{UnlengthedTable}_Code").Should().BeTrue();
        connector.IndexCreationFailures.Should().BeEmpty();

        Insert(UnlengthedTable, null).Should().BeNull();
        Insert(UnlengthedTable, null).Should().BeNull();
        Insert(UnlengthedTable, "C-1").Should().BeNull();
        Insert(UnlengthedTable, "C-1").Should().Be(1062);
    }
}
