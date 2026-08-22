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
/// TASK-273 — partial unique indexes on PostgreSQL, both polarities.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL was never <i>broken</i> by the NULL-equality defect (it treats NULLs as distinct, measured on
/// 16.15), so <c>WhereNotNull</c> here is a portability pin rather than a fix. <c>WhereNull</c> is not: it is
/// the "unique among rows that are not soft-deleted" constraint, which no shape of full index can express,
/// and this is the provider consumers actually deploy on.
/// </para>
/// <para>
/// The identifier half is why this suite must run live at all: predicate columns are emitted <b>bare</b>, and
/// on PostgreSQL a quoted <c>"ExternalId"</c> cannot resolve the case-folded column <c>CreateTable</c>
/// created (<c>42703</c>, the TASK-245/209 family). A green SQLite run says nothing about that, and
/// <c>CreateIndexes</c> records index failures instead of raising them — so this asserts the catalogue.
/// </para>
/// <para>Gated on <c>BIRKO_PG_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </remarks>
public class PartialIndexLiveTests : IDisposable
{
    private const string TableName = "PgPartialRows";
    private const string ExtIdIndex = "ux_pgpartial_extid";
    private const string LiveIndex = "ux_pgpartial_live";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public PartialIndexLiveTests(ITestOutputHelper output) => _output = output;

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
    private static PostgreSQLConnector NewConnector() => new(Settings());

    [Table(TableName)]
    [CompositeIndex(ExtIdIndex, nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
        WhereNotNull = new[] { nameof(ExternalId) })]
    [CompositeIndex(LiveIndex, nameof(TenantGuid), nameof(Number), IsUnique = true,
        WhereNull = new[] { nameof(DeletedAt) })]
    public class PgPartialRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? ExternalId { get; set; }
        public string? Number { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? Insert(Guid tenant, string? externalId, string? number, DateTime? deletedAt)
    {
        try
        {
            using var conn = new NpgsqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO \"{TableName}\" (guid, createdat, updatedat, tenantguid, externalid, number, deletedat) "
                            + "VALUES (@g, now(), now(), @t, @e, @n, @d)";
            cmd.Parameters.AddWithValue("@g", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@t", tenant);
            cmd.Parameters.AddWithValue("@e", (object?)externalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)number ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", (object?)deletedAt ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    private static string? IndexDefinition(string index)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE tablename = @t AND indexname = @i";
        // NOT lower-cased: CreateTable quotes the table name, so the catalogue holds it PascalCase while
        // every column is folded (TASK-209/211). Getting this wrong made the query return null and looked
        // exactly like the index never being created.
        cmd.Parameters.AddWithValue("@t", TableName);
        cmd.Parameters.AddWithValue("@i", index);
        return cmd.ExecuteScalar() as string;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\""); } catch { }
    }

    /// <summary>
    /// Both predicates reach the catalogue as real partial indexes. Asserted against <c>pg_indexes</c>
    /// rather than "CreateTable did not throw", because schema-ensure records index failures instead of
    /// raising them — the TASK-209 lesson about this layer swallowing.
    /// </summary>
    [Fact]
    public void Both_predicate_polarities_are_created_as_partial_indexes()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(PgPartialRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        IndexDefinition(ExtIdIndex).Should().Contain("WHERE (externalid IS NOT NULL)");
        IndexDefinition(LiveIndex).Should().Contain("WHERE (deletedat IS NULL)");
    }

    /// <summary>
    /// The soft-delete constraint, both directions: a number may be reused once its previous holder is
    /// soft-deleted, and may not be reused while that holder is live. A full unique index gets the second
    /// half right and the first half wrong (measured: it rejects, on all four providers).
    /// </summary>
    [Fact]
    public void A_where_null_index_is_unique_among_live_rows_only()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");
        NewConnector().CreateTable(new[] { typeof(PgPartialRow) });

        var tenant = Guid.NewGuid();

        Insert(tenant, null, "DOC-1", new DateTime(2020, 1, 1)).Should().BeNull("a soft-deleted holder");
        Insert(tenant, null, "DOC-1", null).Should().BeNull("the live row may reuse the number");
        Insert(tenant, null, "DOC-1", null).Should().Be("23505", "but two LIVE rows may not share it");
        Insert(tenant, null, "DOC-1", new DateTime(2021, 1, 1)).Should().BeNull(
            "and any number of soft-deleted rows may share it");
    }

    /// <summary>
    /// The <c>WhereNotNull</c> polarity, for portability rather than for a defect: PostgreSQL admits many
    /// NULLs under a full index too, so what this pins is that adding the predicate does not break the
    /// provider that never needed it — including that the bare column resolves (a quoted one is 42703 here).
    /// </summary>
    [Fact]
    public void A_where_not_null_index_still_enforces_uniqueness_where_the_value_is_set()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");
        NewConnector().CreateTable(new[] { typeof(PgPartialRow) });

        var tenant = Guid.NewGuid();

        Insert(tenant, null, null, null).Should().BeNull();
        Insert(tenant, null, null, null).Should().BeNull("NULLs are distinct here — always were");
        Insert(tenant, "EXT-1", null, null).Should().BeNull();
        Insert(tenant, "EXT-1", null, null).Should().Be("23505");
    }
}
