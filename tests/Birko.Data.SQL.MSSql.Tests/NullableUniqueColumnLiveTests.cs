using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-275 — <c>[UniqueField]</c> on a nullable column, on the provider where the inline form is broken.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server treats NULLs as <b>equal</b> for a UNIQUE constraint, so an inline
/// <c>Code NVARCHAR(255) NULL UNIQUE</c> admits one NULL row and rejects every later row that leaves the
/// column unset — <c>Msg 2627</c>, measured on 2022 — and that is the ordinary case, since the column is
/// nullable precisely because most rows have no value. A predicate cannot be attached to an inline
/// constraint (<c>Msg 156</c>), so the constraint is now expressed as
/// <c>CREATE UNIQUE INDEX … WHERE col IS NOT NULL</c> instead, reusing TASK-273's machinery.
/// </para>
/// <para>
/// <b>Note the error code changes with the shape</b>: a violated constraint is <c>2627</c>, a violated unique
/// <i>index</i> is <c>2601</c>. Asserting the wrong one would pass for the wrong reason.
/// </para>
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class NullableUniqueColumnLiveTests : IDisposable
{
    private const string NullableTable = "MsNullableUnique";
    private const string RequiredTable = "MsRequiredUnique";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public NullableUniqueColumnLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    /// <summary>Nullable in the framework's eyes — reference types are nullable unless [RequiredField].</summary>
    [Table(NullableTable)]
    public class NullableRow : AbstractDatabaseLogModel
    {
        [UniqueField]
        [MaxLengthField(64)]
        public string? Code { get; set; }
    }

    /// <summary>[RequiredField] keeps the UNIQUE inline — the DDL for this must not change.</summary>
    [Table(RequiredTable)]
    public class RequiredRow : AbstractDatabaseLogModel
    {
        [UniqueField]
        [RequiredField]
        [MaxLengthField(64)]
        public string Code { get; set; } = null!;
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int? Insert(string table, string? code)
    {
        try
        {
            using var connection = new SqlConnection(Settings().GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO [{table}] ([Guid], [CreatedAt], [UpdatedAt], [Code]) "
                                + "VALUES (NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), @c)";
            command.Parameters.AddWithValue("@c", (object?)code ?? DBNull.Value);
            command.ExecuteNonQuery();
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static string? FilterOf(string table, string index)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT filter_definition FROM sys.indexes WHERE name = @i AND object_id = OBJECT_ID(@t)";
        command.Parameters.AddWithValue("@i", index);
        command.Parameters.AddWithValue("@t", table);
        return command.ExecuteScalar() as string;
    }

    private static int InlineUniqueConstraints(string table)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(@t) AND type = 'UQ'";
        command.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{NullableTable}]"); } catch { }
        try { Exec($"DROP TABLE IF EXISTS [{RequiredTable}]"); } catch { }
    }

    /// <summary>
    /// The fix, both directions: many rows may leave the column unset, and two rows may not share a value.
    /// Before this the second NULL row was rejected with <c>Msg 2627</c>.
    /// </summary>
    [Fact]
    public void A_nullable_unique_column_admits_many_nulls_and_still_rejects_a_duplicate()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{NullableTable}]");

        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(NullableRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        InlineUniqueConstraints(NullableTable).Should().Be(0,
            "the inline UNIQUE is gone — it is what could not admit a second NULL");
        FilterOf(NullableTable, $"ux_{NullableTable}_Code").Should().Be("([Code] IS NOT NULL)",
            "and a filtered unique index carries the constraint instead");

        Insert(NullableTable, null).Should().BeNull("first row with no code");
        Insert(NullableTable, null).Should().BeNull("second row with no code — this was Msg 2627");
        Insert(NullableTable, null).Should().BeNull("and a third");

        Insert(NullableTable, "C-1").Should().BeNull();
        Insert(NullableTable, "C-1").Should().Be(2601,
            "uniqueness still enforced where the value is set — 2601 for an index, not 2627 for a constraint");
    }

    /// <summary>
    /// A <c>[RequiredField]</c> unique column keeps the inline constraint: nothing to fix, so nothing changes.
    /// This is the pin that stops the fix widening into columns that were never broken.
    /// </summary>
    [Fact]
    public void A_required_unique_column_keeps_its_inline_constraint()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{RequiredTable}]");

        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(RequiredRow) });

        InlineUniqueConstraints(RequiredTable).Should().Be(1, "still an inline UNIQUE constraint");
        FilterOf(RequiredTable, $"ux_{RequiredTable}_Code").Should().BeNull("and no index was synthesised");

        Insert(RequiredTable, "C-1").Should().BeNull();
        Insert(RequiredTable, "C-1").Should().Be(2627, "a constraint violation, not an index one");
    }

    /// <summary>
    /// TASK-257's bounding must still apply to the moved column: <see cref="AbstractField.IsInIndexKey"/> is
    /// true via <c>IsUnique</c> whichever shape carries the uniqueness, so an unlengthed string is still
    /// <c>NVARCHAR(255)</c> rather than <c>NVARCHAR(MAX)</c> — which the synthesised index requires, since
    /// <c>MAX</c> cannot be an index key (Msg 1919).
    /// </summary>
    [Fact]
    public void An_unlengthed_nullable_unique_column_is_still_bounded()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [MsUnlengthedNullableUnique]");

        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(UnlengthedRow) });

        try
        {
            connector.IndexCreationFailures.Should().BeEmpty(
                "the column must be bounded, or the synthesised unique index cannot be built at all");
            FilterOf("MsUnlengthedNullableUnique", "ux_MsUnlengthedNullableUnique_Code")
                .Should().Be("([Code] IS NOT NULL)");
        }
        finally
        {
            try { Exec("DROP TABLE IF EXISTS [MsUnlengthedNullableUnique]"); } catch { }
        }
    }

    [Table("MsUnlengthedNullableUnique")]
    public class UnlengthedRow : AbstractDatabaseLogModel
    {
        [UniqueField]
        public string? Code { get; set; }
    }
}
