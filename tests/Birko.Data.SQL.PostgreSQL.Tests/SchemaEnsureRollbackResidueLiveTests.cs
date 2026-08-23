using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-244 — whether a schema-ensure that ran inside a caller's transaction boundary is remembered, on
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// A store remembers its initialization only when the DDL that performed it would survive a rollback of
/// the ambient boundary (<c>AbstractConnector.DdlSurvivesRollback</c>). PostgreSQL has transactional DDL,
/// so a rollback removes the table and the store must <b>not</b> remember — it re-runs schema-ensure on the
/// next operation. MySQL answers the opposite, and its suite asserts that.
/// </para>
/// <para>
/// Live rather than SQLite because the answer derives from <c>SupportsTransactionalDdl</c>, which is
/// exactly the flag that differs per provider. Gated on <c>BIRKO_PG_HOST</c>; set
/// <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails instead of skipping.
/// </para>
/// </remarks>
public class SchemaEnsureRollbackResidueLiveTests : IDisposable
{
    private const string TableName = "PgResidueRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaEnsureRollbackResidueLiveTests(ITestOutputHelper output) => _output = output;

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

    [Table(TableName)]
    public class ResidueRow : AbstractLogModel
    {
        [MaxLengthField(64)]
        public string? Name { get; set; }
    }

    private static AsyncPostgreSQLStore<ResidueRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<ResidueRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Exec(string sql)
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists()
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @t";
        // Quoted by CreateTable, so the catalogue holds it PascalCase (TASK-209).
        command.Parameters.AddWithValue("@t", TableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void DropTable() => Exec($"DROP TABLE IF EXISTS \"{TableName}\"");

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { DropTable(); } catch { }
    }

    /// <summary>
    /// The chain this task exists to break: schema-ensure inside a boundary, boundary rolls back, then an
    /// ordinary write on the SAME store instance. It must land — either because the DDL survived, or
    /// because the store re-ran schema-ensure.
    /// </summary>
    [Fact]
    public async Task A_write_after_a_rolled_back_schema_ensure_still_lands()
    {
        if (!RequireServer()) return;
        DropTable();

        var store = NewStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "first attempt" });
            await uow.RollbackAsync();
        }

        TableExists().Should().BeFalse(
            "PostgreSQL DDL is transactional, so the CREATE TABLE went with the rollback");

        // Same store instance, no boundary. This is the operation that silently lost its row before.
        await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "second attempt" });

        TableExists().Should().BeTrue("the store must have re-run schema-ensure");
        var read = await store.ReadAsync(x => x.Name == "second attempt");
        read.Should().NotBeNull("the write reported success, so the row must be readable");
    }

    /// <summary>
    /// The per-store transaction door must reach the same answer as the ambient one — the half of this
    /// task's acceptance about the two doors agreeing.
    /// </summary>
    [Fact]
    public async Task The_per_store_door_agrees_with_the_ambient_door()
    {
        if (!RequireServer()) return;
        DropTable();

        var store = NewStore();

        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "per-store door" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        TableExists().Should().BeFalse(
            "schema-ensure now enters the store's transaction scope, so this door puts the DDL exactly where "
          + "the ambient door does — before the fix it ran on a connection of its own and committed outside "
          + "the caller's transaction");
    }

    /// <summary>
    /// The capability itself, both sides. Without this the PostgreSQL answer is only ever implied by a
    /// table-survives assertion, and making the flag always-true would break nothing here.
    /// </summary>
    [Fact]
    public async Task DdlSurvivesRollback_is_false_inside_a_boundary_on_postgresql()
    {
        if (!RequireServer()) return;

        var connector = new PostgreSQLConnector(Settings());
        connector.DdlSurvivesRollback.Should().BeTrue("outside a boundary there is nothing that could undo it");

        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using (AmbientSqlTransaction.Enter(Settings().GetId(), connection, transaction))
        {
            connector.DdlSurvivesRollback.Should().BeFalse(
                "PostgreSQL DDL is transactional, so DDL issued inside the boundary dies with it");
        }

        connector.DdlSurvivesRollback.Should().BeTrue("the boundary is gone again");
        transaction.Rollback();
    }
}
