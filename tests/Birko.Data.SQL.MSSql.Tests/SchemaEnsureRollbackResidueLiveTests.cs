using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-244 — whether a schema-ensure that ran inside a caller's transaction boundary is remembered, on
/// SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// A store remembers its initialization only when the DDL that performed it would survive a rollback of
/// the ambient boundary (<c>AbstractConnector.DdlSurvivesRollback</c>). SQL Server has transactional DDL,
/// so a rollback removes the table and the store must <b>not</b> remember — it re-runs schema-ensure on the
/// next operation. MySQL answers the opposite, and its suite asserts that.
/// </para>
/// <para>
/// Live rather than SQLite because the answer derives from <c>SupportsTransactionalDdl</c>, which is
/// exactly the flag that differs per provider. Gated on <c>BIRKO_MSSQL_HOST</c>; set
/// <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails instead of skipping.
/// </para>
/// </remarks>
public class SchemaEnsureRollbackResidueLiveTests : IDisposable
{
    private const string TableName = "MsResidueRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    /// <summary>The database every other class in this suite uses, and which this one deliberately avoids.</summary>
    internal static string SharedDatabase => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    /// <summary>
    /// TASK-276 — this class gets a database of its own, and therefore a <b>connector</b> of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not cosmetic, and not about the tables. <c>DataBase.GetConnector</c> caches one connector per
    /// (type, settings id) for the life of the process, and <c>RemoteSettings.GetId()</c> is
    /// <c>{Location}:{Name}:{UserName}:{Port}</c> — so every class here built the *same* id and shared one
    /// connector. <c>AbstractConnector.SchemaGeneration</c> lives on that connector, and 13 of this
    /// suite's ~20 classes drop a table under an initialised store, each bump invalidating every store's
    /// remembered initialisation (TASK-288's healing, which is <b>correct</b> in production). This class's
    /// tests deliberately leave a store believing it is initialised with its table gone, so a sibling's
    /// bump made the store re-initialise, re-create the dropped table, and the expected failure never
    /// came — measured at roughly 1 run in 5 in-suite and 8/8 clean in isolation.
    /// </para>
    /// <para>
    /// ⚠ <b>Do not "fix" a failure here by weakening an assertion.</b> What they pin is TASK-277's rule —
    /// a write to a missing table must never report success — and that rule is right. Only the isolation
    /// was ever wrong.
    /// </para>
    /// <para>
    /// ⚠ <b>And do not replace this with a shared xUnit collection.</b> That is how the TimescaleDB twin
    /// was fixed (TASK-303) and it does not port: there the overlap was 5 classes, here it is 13 of ~20,
    /// which is <c>"parallelizeTestCollections": false</c> in all but name — the fix TASK-276 explicitly
    /// forbids, because it hides the coupling rather than removing it. A separate settings id makes this
    /// class <b>immune by construction</b>: a class added later cannot reach this connector at all,
    /// whereas a collection has to be remembered and extended.
    /// </para>
    /// </remarks>
    private static string Database => SharedDatabase + "_residue";

    private readonly ITestOutputHelper _output;

    public SchemaEnsureRollbackResidueLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            EnsureDatabase();
            return true;
        }
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    /// <summary>
    /// Creates this class's own database if it is absent, so the isolation above costs the operator no
    /// extra setup step — the suite is run by setting <c>BIRKO_MSSQL_HOST</c> and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>CREATE DATABASE</c> cannot run against the database it creates, so this connects to
    /// <c>master</c> — the one place in this class that does not use <see cref="Settings"/>. Idempotent:
    /// every test's gate calls it.
    /// </remarks>
    private static void EnsureDatabase()
    {
        var master = new MSSqlSettings(Host!, "master", User, Password, Port) { TrustServerCertificate = true };
        using var connection = new SqlConnection(master.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        // The name is a compile-time constant plus an operator-supplied env var, and QUOTENAME contains it
        // regardless; a database name cannot be a parameter in DDL.
        // EXEC will not take a concatenated expression — it needs a variable, hence sp_executesql.
        command.CommandText =
            "IF DB_ID(@d) IS NULL "
          + "BEGIN "
          + "  DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@d); "
          + "  EXEC sp_executesql @sql; "
          + "END";
        command.Parameters.AddWithValue("@d", Database);
        command.ExecuteNonQuery();
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    [Table(TableName)]
    public class ResidueRow : AbstractDatabaseLogModel
    {
        [MaxLengthField(64)]
        public string? Name { get; set; }
    }

    private static AsyncMSSqlStore<ResidueRow> NewStore()
    {
        var store = new AsyncMSSqlStore<ResidueRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists()
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @t";
        command.Parameters.AddWithValue("@t", TableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void DropTable() => Exec($"DROP TABLE IF EXISTS [{TableName}]");

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
            "SQL Server DDL is transactional, so the CREATE TABLE went with the rollback");

        // Same store instance, no boundary. This is the operation that silently lost its row before.
        await store.CreateAsync(new ResidueRow { Guid = Guid.NewGuid(), Name = "second attempt" });

        TableExists().Should().BeTrue("the store must have re-run schema-ensure");
        // Assert the ROW, not merely a non-null result. On a bulk store the bulk Read(filter) overload
        // hides the single-result one and returns the COLLECTION (§ Conventions), so `NotBeNull` passes on
        // an empty enumerable and proves nothing — that weaker version is what hid a real MSSql failure
        // here. ReadFirstAsync would be the idiomatic single-row call and cannot be used: it emits a
        // LIMIT, and on SQL Server a limit with no offset is Msg 153 (TASK-278).
        var rows = await store.ReadAsync(x => x.Name == "second attempt", null, null, null, default);
        rows.Should().ContainSingle("the write reported success, so the row must be there")
            .Which.Name.Should().Be("second attempt");
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

        using var connection = new SqlConnection(Settings().GetConnectionString());
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
    /// The capability itself, both sides. Without this the SQL Server answer is only ever implied by a
    /// table-survives assertion, and making the flag always-true would break nothing here.
    /// </summary>
    [Fact]
    public async Task DdlSurvivesRollback_is_false_inside_a_boundary_on_sqlserver()
    {
        if (!RequireServer()) return;

        var connector = new MSSqlConnector(Settings());
        connector.DdlSurvivesRollback.Should().BeTrue("outside a boundary there is nothing that could undo it");

        using var connection = new SqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using (AmbientSqlTransaction.Enter(Settings().GetId(), connection, transaction))
        {
            connector.DdlSurvivesRollback.Should().BeFalse(
                "SQL Server DDL is transactional, so DDL issued inside the boundary dies with it");
        }

        connector.DdlSurvivesRollback.Should().BeTrue("the boundary is gone again");
        transaction.Rollback();
    }

    /// <summary>
    /// TASK-277 — a write against a table that does not exist must FAIL rather than reporting success.
    /// </summary>
    /// <remarks>
    /// Until TASK-277 every provider's <c>OnException</c> handler answered a missing table with
    /// <c>DoInit()</c> and a <b>return</b>, so the statement was discarded and the caller told it had
    /// worked — silent data loss for any write whose table is absent for any reason (dropped by hand, a
    /// restore that missed it, a migration that never ran, the wrong database). The shared
    /// <c>AbstractConnector.EnsureSchemaAndReport</c> now ensures the schema and then reports.
    /// <para>
    /// The read contract is deliberately untouched: a missing table on a read is handled in
    /// <c>RunReaderCommandOn</c> and still yields an empty result (TASK-211's decision, with its own
    /// callers).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_write_to_a_missing_table_fails_instead_of_reporting_success()
    {
        if (!RequireServer()) return;
        DropTable();

        var store = NewStore();
        await store.InitAsync();
        DropTable();          // the store now believes it is initialised and the table is gone

        Func<Task> write = async () => await store.CreateAsync(
            new ResidueRow { Guid = Guid.NewGuid(), Name = "lost" });

        await write.Should().ThrowAsync<Exception>(
            "the row cannot be stored, so the caller must not be told it was");

        TableExists().Should().BeFalse(
            "and DoInit() does not create it either — it raises OnInit, which nothing in the framework "
          + "subscribes to; the fix is the report, not a repair that never existed");
    }

    /// <summary>
    /// TASK-276 — the isolation the test above depends on, asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flake this class carried was not a timing bug in any assertion; it was that a sibling class's
    /// deliberate schema escape reached <b>this</b> class's store through a shared connector. Putting the
    /// class on its own database fixes that, and nothing would notice if a later edit put it back — the
    /// consequence is a 1-in-5 failure three suites away, not a compile error. Hence this test.
    /// </para>
    /// <para>
    /// It is deterministic, which the flake never was: rather than hoping for the interleaving, it
    /// provokes an escape on the shared connector <b>synchronously</b> and asserts that this class's
    /// connector did not see it. That is the whole mechanism, with the concurrency removed.
    /// </para>
    /// <para>
    /// ⚠ Note what it does <b>not</b> claim: nothing here says the shared connector's healing is wrong.
    /// The bump asserted on the sibling connector is TASK-288 working. Both halves are asserted, so a
    /// change that stopped the healing altogether would red this too rather than passing quietly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task This_class_does_not_share_a_connector_with_the_rest_of_the_suite()
    {
        if (!RequireServer()) return;

        var mine = Settings();
        var shared = new MSSqlSettings(Host!, SharedDatabase, User, Password, Port) { TrustServerCertificate = true };

        mine.GetId().Should().NotBe(shared.GetId(),
            "GetId() is {Location}:{Name}:{UserName}:{Port} and the database is the only component that can "
          + "differ while still reaching the same server");

        var myConnector = DataBase.GetConnector<MSSqlConnector>(mine);
        var sharedConnector = DataBase.GetConnector<MSSqlConnector>(shared);

        myConnector.Should().NotBeSameAs(sharedConnector,
            "GetConnector caches per (type, settings id), so a distinct id is a distinct connector — which "
          + "is what makes this class immune to a sibling's SchemaGeneration bump");

        // Provoke a real escape on the SHARED connector, synchronously: a store that believes it is
        // initialised, writing to a table that has been dropped underneath it. This is exactly what 13 of
        // this suite's classes do incidentally, and what used to reach into this class.
        var mineBefore = myConnector.SchemaGeneration;
        var sharedBefore = sharedConnector.SchemaGeneration;

        var sharedStore = new AsyncMSSqlStore<SiblingRow>();
        sharedStore.SetSettings(shared);
        await sharedStore.InitAsync();
        ExecOn(shared, $"DROP TABLE IF EXISTS [{SiblingTableName}]");

        try
        {
            await sharedStore.CreateAsync(new SiblingRow { Guid = Guid.NewGuid(), Name = "escape" });
        }
        catch (Exception)
        {
            // Expected: TASK-277 — a write to a missing table reports rather than reporting success. The
            // throw is incidental here; the bump it carries is the subject.
        }
        finally
        {
            try { ExecOn(shared, $"DROP TABLE IF EXISTS [{SiblingTableName}]"); } catch { }
        }

        sharedConnector.SchemaGeneration.Should().BeGreaterThan(sharedBefore,
            "the escape was detected on the connector that owns that database — TASK-288's healing, working");

        myConnector.SchemaGeneration.Should().Be(mineBefore,
            "and it must NOT have reached this class's connector. Before TASK-276 this class shared the "
          + "suite-wide connector, so a sibling's bump invalidated its store's remembered initialisation, "
          + "the store re-created the table the test had just dropped, and "
          + "A_write_to_a_missing_table_fails_instead_of_reporting_success saw its write succeed");
    }

    private const string SiblingTableName = "MsResidueSiblingRows";

    /// <summary>A throwaway entity living in the SHARED database, used only to provoke an escape there.</summary>
    [Table(SiblingTableName)]
    public class SiblingRow : AbstractDatabaseLogModel
    {
        [MaxLengthField(64)]
        public string? Name { get; set; }
    }

    private static void ExecOn(MSSqlSettings settings, string sql)
    {
        using var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
