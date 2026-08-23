using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-244 — whether a schema-ensure that ran inside a caller's transaction boundary is remembered, on
/// MySQL — where the answer is YES, and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// A store remembers its initialization only when the DDL that performed it would survive a rollback of
/// the ambient boundary (<c>AbstractConnector.DdlSurvivesRollback</c>). <b>MySQL is the one provider
/// where that is true</b>: it implicitly commits an open transaction around every DDL statement, so
/// TASK-243 issues schema DDL off the boundary here and the table survives a rollback. Both assertions
/// below are therefore the opposite of the PostgreSQL, SQL Server and SQLite ones, and asserting that
/// opposite is the record of why the providers are allowed to differ at all.
/// </para>
/// <para>
/// Live rather than SQLite because the answer derives from <c>SupportsTransactionalDdl</c>, which is
/// exactly the flag that differs per provider. Gated on <c>BIRKO_MYSQL_HOST</c>; set
/// <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails instead of skipping.
/// </para>
/// </remarks>
public class SchemaEnsureRollbackResidueLiveTests : IDisposable
{
    private const string TableName = "MyResidueRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaEnsureRollbackResidueLiveTests(ITestOutputHelper output) => _output = output;

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

    [Table(TableName)]
    public class ResidueRow : AbstractLogModel
    {
        [MaxLengthField(64)]
        public string? Name { get; set; }
    }

    private static AsyncMySQLStore<ResidueRow> NewStore()
    {
        var store = new AsyncMySQLStore<ResidueRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Exec(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists()
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t";
        command.Parameters.AddWithValue("@t", TableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void DropTable() => Exec($"DROP TABLE IF EXISTS `{TableName}`");

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

        TableExists().Should().BeTrue(
            "THE OPPOSITE OF THE OTHER THREE PROVIDERS, on purpose. MySQL implicitly commits an open "
          + "transaction around every DDL statement, so TASK-243 issues schema DDL OFF the boundary "
          + "here (SupportsTransactionalDdl is false for MySQL alone) and the table survives the "
          + "rollback — which is also why the store legitimately DOES remember its initialization "
          + "on this provider");

        // Same store instance, no boundary. This is the operation that silently lost its row before.
        await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "second attempt" });

        TableExists().Should().BeTrue("it survived the rollback; there was nothing to re-run");
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

        using var connection = new MySqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "per-store door" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        TableExists().Should().BeTrue(
            "the two doors must agree, and on MySQL both leave the table in place: the DDL is issued off "
          + "the boundary either way (TASK-243), so a rollback cannot take it");
    }

    /// <summary>
    /// The capability itself, both sides. Without this the MySQL answer is only ever implied by a
    /// table-survives assertion, and a flag that is always false would silently cost this provider a re-run per operation with no test noticing.
    /// </summary>
    [Fact]
    public async Task DdlSurvivesRollback_is_true_inside_a_boundary_on_mysql()
    {
        if (!RequireServer()) return;

        var connector = new MySQLConnector(Settings());
        connector.DdlSurvivesRollback.Should().BeTrue("outside a boundary there is nothing that could undo it");

        using var connection = new MySqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using (AmbientSqlTransaction.Enter(Settings().GetId(), connection, transaction))
        {
            connector.DdlSurvivesRollback.Should().BeTrue(
                "MySQL implicitly commits around every DDL statement, so DoDdlCommand suppresses the ambient and the statement is durable the moment it runs — this is the ONE provider where the answer is true inside a boundary, and it is why the store may remember here");
        }

        connector.DdlSurvivesRollback.Should().BeTrue("the boundary is gone again");
        transaction.Rollback();
    }
}
