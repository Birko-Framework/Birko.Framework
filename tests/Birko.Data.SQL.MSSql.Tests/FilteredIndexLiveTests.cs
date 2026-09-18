using System;
using System.Collections.Generic;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-273 — <c>WhereNotNull</c> / <c>WhereNull</c> on SQL Server, the provider the feature exists for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this provider and not the others.</b> SQL Server treats NULLs as <b>equal</b> for uniqueness, so a
/// full unique index over a nullable column admits exactly one NULL row and rejects the second ordinary row
/// (measured 2026-08-22 on 16.0.4265.3: <c>Msg 2601</c>). PostgreSQL, SQLite and MySQL treat them as
/// distinct and were never broken. So the without-predicate case here is a *defect reproduction*, and the
/// with-predicate case is the fix.
/// </para>
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </remarks>
public class FilteredIndexLiveTests : IDisposable
{
    private const string TableName = "MsFilteredRows";
    private const string UniqueIndex = "ux_msfiltered_extid";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public FilteredIndexLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <remarks>
    /// <c>AbstractDatabaseLogModel</c> for the reason <c>DeclaredIndexLiveTests</c> records: the plain
    /// <c>AbstractLogModel</c> leaves <c>CreatedAt</c> at 0001-01-01, below SQL Server's <c>datetime</c>
    /// floor of 1753.
    /// <para>
    /// <c>ExternalId</c> is deliberately <b>unlengthed</b> — the shape a consumer actually writes. TASK-257
    /// makes it <c>NVARCHAR(255)</c> because it is an index key; before that it was <c>TEXT</c> and no index
    /// over it could be built at all (<c>Msg 1919</c>), which is why this gap only became reachable then.
    /// </para>
    /// </remarks>
    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
        WhereNotNull = new[] { nameof(ExternalId) })]
    public class MsFilteredRow : AbstractDatabaseLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? ExternalId { get; set; }
    }

    /// <summary>The same entity WITHOUT the predicate — the defect, kept as a live reproduction.</summary>
    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(ExternalId), IsUnique = true)]
    public class MsUnfilteredRow : AbstractDatabaseLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? ExternalId { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int? Insert(Guid tenant, string? externalId)
    {
        try
        {
            using var conn = new SqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO [{TableName}] ([Guid], [CreatedAt], [UpdatedAt], [TenantGuid], [ExternalId]) "
                            + "VALUES (NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), @t, @e)";
            cmd.Parameters.AddWithValue("@t", tenant);
            cmd.Parameters.AddWithValue("@e", (object?)externalId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static string? FilterDefinition()
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT filter_definition FROM sys.indexes WHERE name = @i AND object_id = OBJECT_ID(@t)";
        cmd.Parameters.AddWithValue("@i", UniqueIndex);
        cmd.Parameters.AddWithValue("@t", TableName);
        return cmd.ExecuteScalar() as string;
    }

    private static MSSqlConnector NewConnector() => new(Settings());

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{TableName}]"); } catch { }
    }

    /// <summary>
    /// The fix, both directions in one test — a one-directional assertion cannot tell a working filtered
    /// index from an index that was never created.
    /// </summary>
    [Fact]
    public void A_where_not_null_unique_index_admits_many_nulls_and_still_rejects_a_duplicate()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MsFilteredRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        FilterDefinition().Should().Be("([ExternalId] IS NOT NULL)",
            "the predicate has to reach the catalogue — 'CreateTable did not throw' proves nothing here, "
          + "because CreateIndexes records index failures instead of raising them (TASK-204)");

        var tenant = Guid.NewGuid();

        Insert(tenant, null).Should().BeNull("first row with no external id");
        Insert(tenant, null).Should().BeNull("second row with no external id — this is what Msg 2601'd before");
        Insert(tenant, null).Should().BeNull("and a third, to show it is not an off-by-one");

        Insert(tenant, "EXT-1").Should().BeNull("first row that HAS an external id");
        Insert(tenant, "EXT-1").Should().Be(2601, "uniqueness must still be enforced where the value is set");

        Insert(Guid.NewGuid(), "EXT-1").Should().BeNull("a different tenant may reuse the id — it is a composite");
    }

    /// <summary>
    /// The defect, live: the identical entity without the predicate rejects the second ordinary row. This is
    /// what makes the test above a fix rather than a tautology.
    /// </summary>
    [Fact]
    public void Without_the_predicate_the_second_null_row_is_rejected()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MsUnfilteredRow) });
        FilterDefinition().Should().BeNull("no predicate declared");

        var tenant = Guid.NewGuid();
        Insert(tenant, null).Should().BeNull();
        Insert(tenant, null).Should().Be(2601,
            "SQL Server treats NULLs as equal, so a full unique index over a nullable column breaks ordinary "
          + "inserts — the whole reason this feature exists");
    }

    /// <summary>
    /// <b>Pins the Msg 1934 risk that turned out not to exist.</b> SQL Server requires a specific SET-option
    /// state for any DML against a table carrying a filtered index, and this framework never sets SET
    /// options — it relies entirely on <c>Microsoft.Data.SqlClient</c>'s defaults. Measured: they satisfy it
    /// (<c>ARITHABORT</c> reads 0 and it still works, because <c>ANSI_WARNINGS ON</c> implies it). If a
    /// future driver or connection-string change breaks that, it fails here rather than in production.
    /// </summary>
    [Fact]
    public void Dml_works_against_a_filtered_index_on_driver_defaults()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        NewConnector().CreateTable(new[] { typeof(MsFilteredRow) });

        var tenant = Guid.NewGuid();
        Insert(tenant, "EXT-DML").Should().BeNull("INSERT");

        Action update = () => Exec($"UPDATE [{TableName}] SET [ExternalId] = 'EXT-DML-2' WHERE [ExternalId] = 'EXT-DML'");
        update.Should().NotThrow("UPDATE");

        Action delete = () => Exec($"DELETE FROM [{TableName}] WHERE [ExternalId] = 'EXT-DML-2'");
        delete.Should().NotThrow("DELETE");
    }

    /// <summary>
    /// <b>The feature's documented limit.</b> Schema-ensure matches an index by NAME, so editing the
    /// predicate on an entity whose index already exists changes nothing — the old filter survives. Pinned
    /// so it reads as a known limit rather than being rediscovered as a bug; the remedy is to drop the index
    /// by hand. Same position as TASK-257's columns and TASK-245's same-name-different-columns case.
    /// </summary>
    [Fact]
    public void A_changed_predicate_is_not_applied_to_an_existing_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");

        NewConnector().CreateTable(new[] { typeof(MsFilteredRow) });
        FilterDefinition().Should().Be("([ExternalId] IS NOT NULL)");

        // Same table, same index name, no predicate — a schema-ensure for the "edited" entity.
        NewConnector().CreateTable(new[] { typeof(MsUnfilteredRow) });

        FilterDefinition().Should().Be("([ExternalId] IS NOT NULL)",
            "the guard matches on index name only, so the original filtered index survives untouched");
    }

    /// <summary>
    /// Offline pin for this provider's own emitter: predicate columns are bracket-quoted here, matching its
    /// key-column list rather than the base's bare spelling, and the tail sits inside the guarded form.
    /// </summary>
    [Fact]
    public void The_mssql_emitter_brackets_predicate_columns_inside_the_guard()
    {
        var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ux_x", Unique = true };
        index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "TenantGuid", Order = 0 });
        index.Predicates.Add(new Birko.Data.SQL.Tables.IndexPredicate { ColumnName = "ExternalId" });

        var sql = new MSSqlConnector(new MSSqlSettings("localhost", "db", "sa", "p")).CreateIndexSql("T", index);

        sql.Should().Be("IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='ux_x' AND object_id=OBJECT_ID('T')) "
                      + "CREATE UNIQUE INDEX [ux_x] ON [T] ([TenantGuid]) WHERE [ExternalId] IS NOT NULL");
    }

    /// <summary>Criterion 7 on this override: no predicate, byte-identical statement.</summary>
    [Fact]
    public void The_mssql_emitter_is_byte_identical_without_predicates()
    {
        var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ux_x", Unique = true };
        index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "TenantGuid", Order = 0 });

        new MSSqlConnector(new MSSqlSettings("localhost", "db", "sa", "p")).CreateIndexSql("T", index)
            .Should().Be("IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='ux_x' AND object_id=OBJECT_ID('T')) "
                       + "CREATE UNIQUE INDEX [ux_x] ON [T] ([TenantGuid])");
    }
}
