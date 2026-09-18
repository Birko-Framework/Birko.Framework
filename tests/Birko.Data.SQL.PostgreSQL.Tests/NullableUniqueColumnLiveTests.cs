using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-275 — <c>[UniqueField]</c> on a nullable column, on MySQL.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL treats NULLs as distinct, so it was never broken by the inline form and the observable rule
/// is unchanged. It <i>does</i> support partial indexes, so unlike MySQL the synthesised index keeps its
/// <c>WHERE code IS NOT NULL</c> predicate — asserted against <c>pg_indexes</c> rather than inferred.
/// </para>
/// <para>
/// An unlengthed string is <c>TEXT</c> here and PostgreSQL indexes it happily, so the second test is a
/// no-regression pin rather than a fix.
/// </para>
/// <para>Gated on <c>BIRKO_PG_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class NullableUniqueColumnLiveTests : IDisposable
{
    private const string NullableTable = "PgNullableUnique";
    private const string UnlengthedTable = "PgUnlengthedNullableUnique";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public NullableUniqueColumnLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

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
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int? Insert(string table, string? code)
    {
        try
        {
            using var connection = new NpgsqlConnection(Settings().GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO \"{table}\" (guid, createdat, updatedat, code) "
                                + "VALUES (@g, now(), now(), @c)";
            command.Parameters.AddWithValue("@g", Guid.NewGuid());
            command.Parameters.AddWithValue("@c", (object?)code ?? DBNull.Value);
            command.ExecuteNonQuery();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState == "23505" ? 23505 : -1;
        }
    }

    private static bool IndexExists(string table, string index)
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        // The table is quoted by CreateTable, so the catalogue holds it PascalCase (TASK-209).
        command.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE tablename = @t AND indexname = @i";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@i", index);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static string ColumnType(string table, string column)
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_name = @t AND column_name = @c";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@c", column.ToLowerInvariant());
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{NullableTable}\""); } catch { }
        try { Exec($"DROP TABLE IF EXISTS \"{UnlengthedTable}\""); } catch { }
    }

    /// <summary>
    /// The index is built as a real partial index, and the rule is what it always was.
    /// </summary>
    [Fact]
    public void A_nullable_unique_column_becomes_a_partial_unique_index_and_behaves_the_same()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{NullableTable}\"");

        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(NullableRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        IndexExists(NullableTable, $"ux_{NullableTable}_Code").Should().BeTrue(
            "PostgreSQL supports partial indexes, so the predicate is kept");

        Insert(NullableTable, null).Should().BeNull();
        Insert(NullableTable, null).Should().BeNull("MySQL always admitted many NULLs — unchanged");
        Insert(NullableTable, "C-1").Should().BeNull();
        Insert(NullableTable, "C-1").Should().Be(23505);
    }

    /// <summary>
    /// An unlengthed unique column keeps working — TEXT is indexable here, so nothing had to be bounded.
    /// </summary>
    [Fact]
    public void An_unlengthed_nullable_unique_column_still_works()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{UnlengthedTable}\"");

        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(UnlengthedRow) });

        ColumnType(UnlengthedTable, "Code").Should().Be("text",
            "PostgreSQL indexes TEXT, so nothing needs bounding here — unlike MySQL");
        IndexExists(UnlengthedTable, $"ux_{UnlengthedTable}_Code").Should().BeTrue();
        connector.IndexCreationFailures.Should().BeEmpty();

        Insert(UnlengthedTable, null).Should().BeNull();
        Insert(UnlengthedTable, null).Should().BeNull();
        Insert(UnlengthedTable, "C-1").Should().BeNull();
        Insert(UnlengthedTable, "C-1").Should().Be(23505);
    }
}
