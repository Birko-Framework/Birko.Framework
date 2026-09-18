using System;
using System.Data.Common;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-246, live half — a migration's <c>.Unique()</c> must build a real constraint on a real server.
///
/// <para>
/// The SQLite sibling (<see cref="MigrationUniqueIndexTests"/>) is the primary proof, and this is here
/// because a silently non-unique index is a <b>data-integrity</b> defect and a SQLite-only suite is exactly
/// what let it ship: every pre-existing test in this project built the schema builder with
/// <c>connector: null</c>, taking the raw-SQL fallback that always honoured <c>_unique</c>, so the broken
/// connector path was never executed anywhere.
/// </para>
///
/// <para>
/// PostgreSQL also adds a check SQLite cannot: it case-folds unquoted identifiers, so this doubles as
/// end-to-end proof that the index actually binds to the columns it names (TASK-245's identifier fix) rather
/// than being created against something that does not resolve.
/// </para>
///
/// <para>Gated on <c>BIRKO_PG_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class MigrationUniqueIndexLiveTests : IDisposable
{
    private const string TableName = "MigUxOrders";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public MigrationUniqueIndexLiveTests(ITestOutputHelper output) => _output = output;

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

    private static AbstractConnector Connector() => DataBase.GetConnector<PostgreSQLConnector>(Settings());

    private static NpgsqlConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        return conn;
    }

    private static void Exec(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try
        {
            using var conn = OpenConnection();
            Exec(conn, $"DROP TABLE IF EXISTS \"{TableName}\"");
        }
        catch { }
    }

    private static void FreshTable(DbConnection conn)
    {
        Exec(conn, $"DROP TABLE IF EXISTS \"{TableName}\"");
        // Columns bare, table quoted — exactly what AbstractConnector.CreateTable emits, so PostgreSQL folds
        // the column names and the index has to reference them the same way to resolve at all.
        Exec(conn, $"CREATE TABLE \"{TableName}\" (TenantGuid uuid, Number text, Status text)");
    }

    private static void Insert(DbConnection conn, Guid tenant, string number, string status = "open")
        => Exec(conn, $"INSERT INTO \"{TableName}\" (TenantGuid, Number, Status) "
                    + $"VALUES ('{tenant}', '{number}', '{status}')");

    private static bool IsUnique(DbConnection conn, string indexName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ix.indisunique FROM pg_class i "
                        + "JOIN pg_index ix ON ix.indexrelid = i.oid WHERE i.relname = @i";
        var p = cmd.CreateParameter(); p.ParameterName = "@i"; p.Value = indexName;
        cmd.Parameters.Add(p);
        var value = cmd.ExecuteScalar();
        return value is bool b && b;
    }

    [Fact]
    public void A_migration_declared_unique_index_is_enforced_on_postgresql()
    {
        if (!RequireServer()) return;

        var connector = Connector();
        using var conn = OpenConnection();
        FreshTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex(TableName, "ux_migux_docnum")
            .WithField("TenantGuid")
            .WithField("Number")
            .Unique()
            .Build();

        var tenant = Guid.NewGuid();
        Insert(conn, tenant, "FV2026000001");

        Action duplicate = () => Insert(conn, tenant, "FV2026000001");
        duplicate.Should().Throw<PostgresException>(
            "the migration declared .Unique(), so the server must refuse the duplicate — against the unfixed "
          + "builder the index was created without UNIQUE and this insert succeeded");

        Action otherTenant = () => Insert(conn, Guid.NewGuid(), "FV2026000001");
        otherTenant.Should().NotThrow("uniqueness is over the declared pair, not the Number alone");

        IsUnique(conn, "ux_migux_docnum").Should().BeTrue(
            "and the catalogue agrees — the companion assertion to the enforcement above");
    }

    /// <summary>The guard against "fixing" this by making every migration index unique.</summary>
    [Fact]
    public void A_migration_index_without_unique_stays_non_unique_on_postgresql()
    {
        if (!RequireServer()) return;

        var connector = Connector();
        using var conn = OpenConnection();
        FreshTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex(TableName, "ix_migux_status")
            .WithField("Status")
            .Build();

        Insert(conn, Guid.NewGuid(), "A", "open");
        Action duplicate = () => Insert(conn, Guid.NewGuid(), "B", "open");

        duplicate.Should().NotThrow("no .Unique() was declared");
        IsUnique(conn, "ix_migux_status").Should().BeFalse();
    }
}
