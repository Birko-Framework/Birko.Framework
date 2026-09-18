using System;
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
/// TASK-266 against a real SQL Server — the two halves of that task, which need opposite answers.
///
/// <para>
/// <b>Binary (fixed).</b> A <c>[UniqueField] byte[]</c> entity's table could not be created at all:
/// <c>VARBINARY(MAX)</c> cannot be an index key, and an inline <c>UNIQUE</c> over one raises Msg 1919 +
/// Msg 1750 which <c>TRY/CATCH</c> cannot intercept, so the batch aborts. The column is now bounded and
/// the constraint is asserted from <c>sys.indexes</c> — not from "the DDL did not throw", which
/// § TASK-209 records as worth nothing here because this layer swallows.
/// </para>
/// <para>
/// <b>Wide composite (pinned, deliberately not fixed).</b> <c>IndexedStringColumnLength</c> is 255
/// characters = 510 bytes under <c>NVARCHAR</c>, which caps a single column but not a per-index total. Four
/// such columns is 2040 bytes against a 1700-byte nonclustered limit, and SQL Server <b>creates the index
/// anyway</b>, with a warning, then rejects only those inserts whose actual key exceeds the limit
/// (Msg 1946). The measurement that decides the remedy is <see cref="A_short_row_still_inserts"/>: a table
/// whose values stay short works perfectly, so refusing at DDL would break working code —
/// <c>PredicateScope</c>'s recorded rule that a false refusal is worse than the hole. MySQL, by contrast,
/// refuses this index outright (ERROR 1071), so the quiet form is SQL Server's alone and a framework
/// guard would duplicate one server while regressing the other.
/// </para>
/// <para>
/// Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </para>
/// </summary>
public class BinaryAndWideCompositeIndexLiveTests : IDisposable
{
    private const string BinaryTable = "MsBinLiveUnique";
    private const string BoundedTable = "MsBinLiveBounded";
    private const string WideTable = "MsWideCompositeLive";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public BinaryAndWideCompositeIndexLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>A unique binary key with no declared width — the shape that had no table at all.</summary>
    [Table(BinaryTable)]
    public class HashRow : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public byte[] Hash { get; set; } = Array.Empty<byte>();
    }

    /// <summary>The same with an explicit width, which is what a real hash column wants.</summary>
    [Table(BoundedTable)]
    public class BoundedHashRow : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        [MaxLengthField(16)]
        public byte[] Uuid { get; set; } = Array.Empty<byte>();
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T Scalar<T>(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T))!;
    }

    private static string ColumnType(string table, string column)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT t.name + '(' + CASE WHEN c.max_length = -1 THEN 'MAX' "
            + "ELSE CAST(c.max_length AS VARCHAR(10)) END + ')' "
            + "FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id "
            + "WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c";
        command.Parameters.AddWithValue("@t", table);
        command.Parameters.AddWithValue("@c", column);
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { BinaryTable, BoundedTable, WideTable })
        {
            try { Exec($"DROP TABLE IF EXISTS [{t}]"); } catch { }
        }
    }

    // ───────────────────────────── binary: the fix ─────────────────────────────

    /// <summary>
    /// Criterion 3. The table is created <b>and</b> the unique constraint is present — asserted from
    /// <c>sys.indexes</c>, because <c>CreateTable</c> swallows and records, so "it did not throw" would
    /// pass against a table with no constraint at all.
    /// </summary>
    [Fact]
    public void A_unique_byte_array_entity_gets_its_table_and_its_constraint()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{BinaryTable}]");

        new MSSqlConnector(Settings()).CreateTable(new[] { typeof(HashRow) });

        Scalar<int>($"SELECT COUNT(*) FROM sys.tables WHERE name = '{BinaryTable}'")
            .Should().Be(1, "before TASK-266 Msg 1919 + Msg 1750 aborted the whole CREATE TABLE");
        ColumnType(BinaryTable, "Hash").Should().Be("varbinary(255)");
        Scalar<int>($"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('{BinaryTable}') "
                    + "AND is_unique = 1 AND is_primary_key = 0")
            .Should().BeGreaterThan(0, "the UNIQUE constraint must actually exist, not merely not throw");
    }

    /// <summary>The constraint is enforced, which is the only thing that makes it a constraint.</summary>
    [Fact]
    public void The_unique_binary_constraint_rejects_a_duplicate()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{BinaryTable}]");
        new MSSqlConnector(Settings()).CreateTable(new[] { typeof(HashRow) });

        Exec($"INSERT INTO [{BinaryTable}] (Guid, CreatedAt, UpdatedAt, Hash) "
             + "VALUES (NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 0x0102)");

        Action duplicate = () => Exec(
            $"INSERT INTO [{BinaryTable}] (Guid, CreatedAt, UpdatedAt, Hash) "
            + "VALUES (NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 0x0102)");

        duplicate.Should().Throw<SqlException>().Which.Number.Should().BeOneOf(2601, 2627);
    }

    /// <summary>A declared width reaches the real column, so a 16-byte key is not padded to 255.</summary>
    [Fact]
    public void A_declared_width_reaches_the_real_column()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{BoundedTable}]");

        new MSSqlConnector(Settings()).CreateTable(new[] { typeof(BoundedHashRow) });

        ColumnType(BoundedTable, "Uuid").Should().Be("varbinary(16)",
            "[MaxLengthField] on a byte[] was silently dropped before TASK-266");
    }

    // ─────────────────── wide composite: pinned, not fixed ───────────────────

    private const string WideDdl =
        "CREATE TABLE [" + WideTable + "] (A NVARCHAR(255), B NVARCHAR(255), C NVARCHAR(255), D NVARCHAR(255))";

    /// <summary>
    /// Three 255-character columns is 1530 bytes and creates cleanly — the control that shows the next
    /// test is about the width and not about composites in general.
    /// </summary>
    [Fact]
    public void A_three_column_composite_is_within_the_key_limit()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{WideTable}]");
        Exec(WideDdl);

        Action create = () => Exec($"CREATE INDEX ix_wide3 ON [{WideTable}](A,B,C)");

        create.Should().NotThrow("1530 bytes is under the 1700-byte nonclustered limit");
    }

    /// <summary>
    /// ⚠ <b>The deferred failure, pinned so it cannot regress unnoticed.</b> 2040 bytes exceeds the limit
    /// and SQL Server still <b>creates</b> the index, warning only that "for some combination of large
    /// values, the insert/update operation will fail".
    /// </summary>
    [Fact]
    public void A_four_column_composite_is_created_despite_exceeding_the_key_limit()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{WideTable}]");
        Exec(WideDdl);

        Action create = () => Exec($"CREATE INDEX ix_wide4 ON [{WideTable}](A,B,C,D)");

        create.Should().NotThrow("SQL Server warns rather than refusing — MySQL is the one that refuses");
        Scalar<int>($"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('{WideTable}') "
                    + "AND name = 'ix_wide4'")
            .Should().Be(1);
    }

    /// <summary>Then a max-width row is rejected — Msg 1946, at INSERT rather than at DDL.</summary>
    [Fact]
    public void A_max_width_row_is_rejected_at_insert_time()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{WideTable}]");
        Exec(WideDdl);
        Exec($"CREATE INDEX ix_wide4 ON [{WideTable}](A,B,C,D)");

        Action insert = () => Exec(
            $"INSERT INTO [{WideTable}] VALUES (REPLICATE('a',255), REPLICATE('b',255), "
            + "REPLICATE('c',255), REPLICATE('d',255))");

        insert.Should().Throw<SqlException>().Which.Number.Should().Be(1946,
            "the index entry of 2040 bytes exceeds the 1700-byte maximum");
    }

    /// <summary>
    /// ⚠ <b>And this is the measurement that decides the remedy.</b> A short row inserts perfectly into
    /// that same over-wide index, so the index is not broken — it is data-dependent. Refusing it at DDL
    /// would therefore break working code, which is why TASK-266 pins this behaviour instead of guarding
    /// it. If this test ever starts failing, the trade-off it records has changed.
    /// </summary>
    [Fact]
    public void A_short_row_still_inserts()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{WideTable}]");
        Exec(WideDdl);
        Exec($"CREATE INDEX ix_wide4 ON [{WideTable}](A,B,C,D)");

        Action insert = () => Exec($"INSERT INTO [{WideTable}] VALUES ('a','b','c','d')");

        insert.Should().NotThrow("a table whose values stay short works today, and must keep working");
        Scalar<int>($"SELECT COUNT(*) FROM [{WideTable}]").Should().Be(1);
    }
}
