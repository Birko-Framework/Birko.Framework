using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// TASK-245 — PostgreSQL could not create any declared index on a PascalCase-columned entity either. Same
/// user-visible symptom as the MySQL half of that task, by a completely different mechanism.
///
/// <para>
/// <c>AbstractConnector.CreateTable</c> quotes the table name and emits <b>column definitions bare</b>, so
/// on PostgreSQL — the one supported provider that case-folds an unquoted identifier — every column is
/// stored folded (<c>status</c>, <c>tenantguid</c>). <c>CreateIndexSql</c>, meanwhile, wrapped each column
/// in <c>QuoteIdentifier</c>, and a quoted <c>"Status"</c> cannot resolve a column stored as
/// <c>status</c>: measured on PostgreSQL 16 as <c>ERROR 42703: column "Status" does not exist</c>. So the
/// index DDL failed, TASK-204 recorded it rather than throwing, and nobody was listening.
/// </para>
///
/// <para>
/// <b>Why no test caught it.</b> Every index end-to-end test in the tree runs on SQLite, which is
/// case-insensitive and therefore cannot distinguish the two spellings. This suite exists so the fix
/// (columns emitted bare, per CLAUDE.md § Conventions) is pinned on the provider where the distinction is
/// real — the seventh instance of that identifier family.
/// </para>
///
/// <para>
/// The assertions query <c>pg_indexes</c> / <c>pg_index</c>. "Nothing threw" proves nothing here: this
/// layer swallows, so a broken index DDL reports success and simply leaves no index behind.
/// </para>
/// </summary>
public class DeclaredIndexLiveTests : IDisposable
{
    private const string TableName = "PgIdxRows";
    private const string UniqueIndex = "ux_pgidxrows_docnum";
    private const string PlainIndex = "ix_pgidxrows_status";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public DeclaredIndexLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    /// <summary>
    /// PascalCase property names are the whole point — they are what the base DDL folds and what a quoted
    /// index column then fails to resolve. A lower-case-named entity would pass either way.
    /// </summary>
    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(Number), IsUnique = true)]
    [CompositeIndex(PlainIndex, nameof(Status), nameof(Number))]
    public class PgIdxRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string Number { get; set; } = null!;

        [MaxLengthField(32)]
        public string Status { get; set; } = null!;
    }

    private static void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\""); } catch { }
    }

    private static PostgreSQLConnector NewConnector() => new(Settings());

    /// <summary>The index's column list in key order, straight from the catalogue.</summary>
    private static List<string> IndexColumns(string index)
    {
        var result = new List<string>();
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT a.attname
FROM pg_class i
JOIN pg_index ix ON ix.indexrelid = i.oid
JOIN pg_class t ON t.oid = ix.indrelid
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
WHERE i.relname = @i AND t.relname = @t
ORDER BY array_position(ix.indkey, a.attnum)";
        cmd.Parameters.AddWithValue("@i", index);
        cmd.Parameters.AddWithValue("@t", TableName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static bool IsUnique(string index)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ix.indisunique FROM pg_class i "
                        + "JOIN pg_index ix ON ix.indexrelid = i.oid WHERE i.relname = @i";
        cmd.Parameters.AddWithValue("@i", index);
        var value = cmd.ExecuteScalar();
        return value is bool b && b;
    }

    private static int IndexCount()
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE tablename = @t AND indexname <> @pk";
        cmd.Parameters.AddWithValue("@t", TableName);
        cmd.Parameters.AddWithValue("@pk", TableName + "_pkey");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static PgIdxRow Row(Guid tenant, string number, string status = "open")
        => new() { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = number, Status = status };

    // ---------------------------------------------------------------- the R4 regression

    [Fact]
    public void Declared_indexes_over_pascalcase_columns_are_created_on_postgresql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(PgIdxRow) });

        // The columns really are stored folded — the premise of the defect, pinned so the test cannot
        // quietly stop being about anything.
        IndexColumns(PlainIndex).Should().Equal(new[] { "status", "number" },
            "PostgreSQL folds the bare column identifiers CreateTable emits, so the index must reference "
          + "them bare too — a quoted \"Status\" raised 42703 and left no index at all");

        IndexColumns(UniqueIndex).Should().Equal(new[] { "tenantguid", "number" });
        IsUnique(UniqueIndex).Should().BeTrue();
        IsUnique(PlainIndex).Should().BeFalse();

        connector.IndexCreationFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task Declared_indexes_are_created_by_the_async_schema_ensure_too()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");

        var connector = NewConnector();
        await connector.CreateTableAsync(new[] { typeof(PgIdxRow) }, CancellationToken.None);

        IndexColumns(UniqueIndex).Should().Equal(new[] { "tenantguid", "number" });
        IndexCount().Should().Be(2);
        connector.IndexCreationFailures.Should().BeEmpty();
    }

    /// <summary>For a UNIQUE index the constraint is the point, not the catalogue row.</summary>
    [Fact]
    public async Task A_declared_unique_index_is_enforced_on_postgresql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");
        NewConnector().CreateTable(new[] { typeof(PgIdxRow) });

        var store = new AsyncPostgreSQLStore<PgIdxRow>();
        store.SetSettings(Settings());

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.CreateAsync(Row(tenantA, "FV2026000001"));
        await store.Invoking(s => s.CreateAsync(Row(tenantA, "FV2026000001")))
                   .Should().ThrowAsync<Exception>("the (tenant, number) pair must be constrained");
        await store.CreateAsync(Row(tenantB, "FV2026000001"));
    }

    /// <summary>
    /// PostgreSQL <i>does</i> support <c>IF NOT EXISTS</c>, so a second schema-ensure is a server-side
    /// no-op and the client-side 1061 tolerance added for MySQL must never come into play here — the base
    /// <see cref="AbstractConnectorBase.IsIndexAlreadyExistsException"/> returning false is what guarantees
    /// that, and this is the assertion of it.
    /// </summary>
    [Fact]
    public void A_second_schema_ensure_is_a_server_side_no_op_on_postgresql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");

        var connector = NewConnector();
        var raised = new List<IndexCreationFailure>();
        connector.OnIndexCreationFailed += raised.Add;

        connector.CreateTable(new[] { typeof(PgIdxRow) });
        connector.CreateTable(new[] { typeof(PgIdxRow) });

        raised.Should().BeEmpty();
        connector.IndexCreationFailures.Should().BeEmpty();
        IndexCount().Should().Be(2, "no duplicate index");

        connector.IsIndexAlreadyExistsException(new Exception("anything")).Should().BeFalse(
            "PostgreSQL emits IF NOT EXISTS, so the condition never reaches the client and the base "
          + "predicate must stay false — no behaviour change off MySQL");
    }

    /// <summary>
    /// The <c>throwIfExists: true</c> door means the same thing here as on MySQL: the conditional clause is
    /// dropped, so an already-present index raises rather than being silently skipped. A flag honoured on
    /// one provider and ignored on three would be the silent-drop shape § Conventions ranks worst.
    /// </summary>
    [Fact]
    public void CreateIndexes_with_throwIfExists_raises_for_an_already_present_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(PgIdxRow) });

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(PgIdxRow));
        var index = table!.Indexes![PlainIndex];

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }, throwIfExists: true))
                 .Should().Throw<Exception>("without the conditional clause PostgreSQL reports 42P07");

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }))
                 .Should().NotThrow("and the default stays an ensure");
    }

    /// <summary>
    /// The index manager's unique path went through a <c>CreateUniqueIndexSql</c> override that quoted its
    /// columns, so it was broken on PostgreSQL for the same reason and separately from schema-ensure.
    /// TASK-245 deleted it in favour of the connector emitter; this is the end-to-end proof.
    /// </summary>
    [Fact]
    public async Task The_index_manager_can_create_a_unique_index_on_postgresql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS \"{TableName}\"");
        NewConnector().CreateTable(new[] { typeof(PgIdxRow) });

        var manager = new Birko.Data.SQL.PostgreSQL.IndexManagement.PostgreSqlIndexManager(NewConnector());
        var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition
        {
            Name = "ux_pgidxrows_manager",
            Unique = true,
            Fields = new[]
            {
                new Birko.Data.Patterns.IndexManagement.IndexField { Name = "TenantGuid" },
                new Birko.Data.Patterns.IndexManagement.IndexField { Name = "Status" }
            }
        };

        await manager.CreateAsync(definition, TableName, CancellationToken.None);

        IndexColumns("ux_pgidxrows_manager").Should().Equal(new[] { "tenantguid", "status" });
        IsUnique("ux_pgidxrows_manager").Should().BeTrue(
            "and it must actually be UNIQUE — ToSqlIndexDefinition used to drop the flag, which is why a "
          + "parallel unique emitter existed at all");
    }
}
