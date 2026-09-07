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
/// TASK-266 against a real MySQL — the binary fix, and the wide-composite behaviour that is
/// <b>opposite</b> to SQL Server's.
///
/// <para>
/// <b>Binary (fixed).</b> MySQL cannot use a BLOB/TEXT column in a key without a key length, so a
/// <c>[UniqueField] byte[]</c> entity's <c>LONGBLOB UNIQUE</c> column raised ERROR 1170 and the table
/// could not be created. The column is now bounded, and the constraint is asserted from
/// <c>information_schema.statistics</c> rather than from "the DDL did not throw" — <c>CreateTable</c>
/// swallows and records (TASK-204), so a non-throwing call proves nothing.
/// </para>
/// <para>
/// <b>Wide composite (pinned, and loud here).</b> Measured on 8.4.11 with utf8mb4 and the
/// <c>dynamic</c> row format: <c>VARCHAR(255)</c> is 1020 bytes, so three columns is 3060 — twelve bytes
/// inside the 3072-byte InnoDB limit — and four is 4080, which MySQL <b>refuses outright</b> with
/// ERROR 1071. SQL Server instead creates the index with a warning and fails later, only for wide values.
/// That asymmetry is why TASK-266 adds no framework guard: on this provider the server already refuses,
/// and on the other a guard would break the short-value case that works today.
/// </para>
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class BinaryAndWideCompositeIndexLiveTests : IDisposable
{
    private const string BinaryTable = "MyBinLiveUnique";
    private const string BoundedTable = "MyBinLiveBounded";
    private const string WideTable = "MyWideCompositeLive";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public BinaryAndWideCompositeIndexLiveTests(ITestOutputHelper output) => _output = output;

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

    [Table(BinaryTable)]
    public class HashRow : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public byte[] Hash { get; set; } = Array.Empty<byte>();
    }

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
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T Scalar<T>(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T))!;
    }

    private static string ColumnType(string table, string column)
        => Scalar<string>(
            "SELECT COLUMN_TYPE FROM information_schema.columns "
            + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'");

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { BinaryTable, BoundedTable, WideTable })
        {
            try { Exec($"DROP TABLE IF EXISTS `{t}`"); } catch { }
        }
    }

    // ───────────────────────────── binary: the fix ─────────────────────────────

    [Fact]
    public void A_unique_byte_array_entity_gets_its_table_and_its_constraint()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{BinaryTable}`");

        new MySQLConnector(Settings()).CreateTable(new[] { typeof(HashRow) });

        Scalar<int>("SELECT COUNT(*) FROM information_schema.tables WHERE TABLE_SCHEMA = DATABASE() "
                    + $"AND TABLE_NAME = '{BinaryTable}'")
            .Should().Be(1, "before TASK-266 ERROR 1170 stopped the table being created at all");
        ColumnType(BinaryTable, "Hash").Should().Be("varbinary(255)");
        Scalar<int>("SELECT COUNT(*) FROM information_schema.statistics WHERE TABLE_SCHEMA = DATABASE() "
                    + $"AND TABLE_NAME = '{BinaryTable}' AND NON_UNIQUE = 0 AND INDEX_NAME <> 'PRIMARY'")
            .Should().BeGreaterThan(0, "the UNIQUE constraint must exist, not merely not throw");
    }

    [Fact]
    public void The_unique_binary_constraint_rejects_a_duplicate()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{BinaryTable}`");
        new MySQLConnector(Settings()).CreateTable(new[] { typeof(HashRow) });

        Exec($"INSERT INTO `{BinaryTable}` (Guid, CreatedAt, UpdatedAt, Hash) "
             + "VALUES (UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), 0x0102)");

        Action duplicate = () => Exec(
            $"INSERT INTO `{BinaryTable}` (Guid, CreatedAt, UpdatedAt, Hash) "
            + "VALUES (UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), 0x0102)");

        duplicate.Should().Throw<MySqlException>().Which.Number.Should().Be(1062,
            "1062 is Duplicate entry — a genuinely enforced constraint");
    }

    [Fact]
    public void A_declared_width_reaches_the_real_column()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{BoundedTable}`");

        new MySQLConnector(Settings()).CreateTable(new[] { typeof(BoundedHashRow) });

        ColumnType(BoundedTable, "Uuid").Should().Be("varbinary(16)",
            "[MaxLengthField] on a byte[] was silently dropped before TASK-266");
    }

    // ─────────────── wide composite: loud here, unlike SQL Server ───────────────

    private const string WideDdl =
        "CREATE TABLE `" + WideTable + "` (A VARCHAR(255), B VARCHAR(255), C VARCHAR(255), D VARCHAR(255))";

    /// <summary>
    /// Three columns is 3060 bytes — inside the 3072-byte limit by twelve. The control that shows the
    /// next test is about width, not composites.
    /// </summary>
    [Fact]
    public void A_three_column_composite_is_just_within_the_key_limit()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{WideTable}`");
        Exec(WideDdl);

        Action create = () => Exec($"CREATE INDEX ix_wide3 ON `{WideTable}`(A,B,C)");

        create.Should().NotThrow("3 x VARCHAR(255) utf8mb4 = 3060 of 3072 bytes");
    }

    /// <summary>
    /// ⚠ <b>The asymmetry with SQL Server, pinned.</b> Four columns is 4080 bytes and MySQL refuses the
    /// index at DDL. SQL Server creates the same index with a warning and fails later at INSERT, for wide
    /// values only. Two providers, opposite behaviour, both correct for themselves — and together the
    /// reason TASK-266 adds no framework-level width guard.
    /// </summary>
    [Fact]
    public void A_four_column_composite_is_refused_at_ddl()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{WideTable}`");
        Exec(WideDdl);

        Action create = () => Exec($"CREATE INDEX ix_wide4 ON `{WideTable}`(A,B,C,D)");

        create.Should().Throw<MySqlException>().Which.Number.Should().Be(1071,
            "Specified key was too long; max key length is 3072 bytes — loud, where SQL Server only warns");
    }
}
