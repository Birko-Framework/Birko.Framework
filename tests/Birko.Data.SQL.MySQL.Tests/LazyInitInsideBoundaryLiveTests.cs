using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-243 — a store whose <b>first</b> operation happens inside a transaction boundary must not destroy
/// that boundary, on the one provider where it did.
///
/// <para>
/// Stores initialise lazily: <c>EnsureInitialized</c> runs in the public CRUD wrapper and issues
/// <c>CREATE TABLE IF NOT EXISTS</c>, which after TASK-240 went onto the <b>ambient boundary's
/// connection</b>. <b>MySQL implicitly commits an open transaction before and after every DDL
/// statement</b>, so the boundary was committed before the caller's own write even ran and the later
/// rollback undid nothing. Measured on MySQL 8.4 with the TASK-242 connector fix fully in place: three
/// rows survived a rolled-back boundary, with no error on the way in and none on the way out.
/// </para>
///
/// <para>
/// This is not a test-only hazard. A host resolving scoped stores per request gets a fresh store instance
/// per request and <c>_initialized</c> lives on the store, so the <i>first</i> request that touches an
/// entity inside a boundary silently loses that boundary. The only evidence is rows that should not
/// exist.
/// </para>
///
/// <para>
/// The fix is <see cref="AbstractConnectorBase.SupportsTransactionalDdl"/>: MySQL returns false, and
/// <c>AbstractConnector.DoDdlCommand</c> then issues schema DDL with the boundary suppressed, on a
/// connection of its own. Safe here for the same reason the defect exists here — MySQL permits the second
/// connection — and measured rather than assumed: an open transaction holding a row lock on a table does
/// not block a concurrent <c>CREATE TABLE IF NOT EXISTS</c> on that table.
/// </para>
///
/// <para>
/// <b>Every test here deliberately skips the warm-up read.</b> That is the entire point: the store must be
/// uninitialised when the boundary opens. A test that warms up first cannot fail on this defect, which is
/// exactly why the sibling <c>BulkTransactionBoundaryLiveTests</c> could not see it.
/// </para>
/// </summary>
public class LazyInitInsideBoundaryLiveTests : IDisposable
{
    private const string TableName = "LazyInitRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public LazyInitInsideBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    public class LazyRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class LazyRowMapping : IModelMapping<LazyRow>
    {
        public void Configure(ModelMap<LazyRow> map)
        {
            map.ToTable(TableName).HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS `{TableName}`"); } catch { }
    }

    /// <summary>Creates the table through a connector of its own, leaving every store uninitialised.</summary>
    private static void FreshTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new LazyRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(LazyRow) });
    }

    /// <summary>Drops the table so the lazy schema-ensure has to genuinely create it.</summary>
    private static void NoTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new LazyRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS `{TableName}`");
    }

    private static AsyncMySQLStore<LazyRow> AsyncStore()
    {
        var store = new AsyncMySQLStore<LazyRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static MySQLStore<LazyRow> SyncStore()
    {
        var store = new MySQLStore<LazyRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static List<LazyRow> Rows(params string[] names)
        => names.Select((n, i) => new LazyRow { Guid = Guid.NewGuid(), Name = n, Amount = i + 1 }).ToList();

    private static int CommittedCount()
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{TableName}`";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool TableExists()
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables "
                        + "WHERE table_schema = DATABASE() AND table_name = @t";
        cmd.Parameters.AddWithValue("@t", TableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    // ---------------------------------------------------------------- the defect

    [Fact]
    public async Task A_bulk_write_from_a_store_initialising_inside_the_boundary_still_rolls_back()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();   // deliberately NOT warmed up

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "the store's lazy CREATE TABLE must not be issued on the boundary's connection — on MySQL that "
          + "implicitly commits it, and against the unfixed code these three rows survived the rollback");
    }

    [Fact]
    public async Task A_single_row_write_from_a_store_initialising_inside_the_boundary_still_rolls_back()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new LazyRow { Guid = Guid.NewGuid(), Name = "only", Amount = 1 });
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "nothing about this is specific to bulk writes — the DDL commits the boundary before ANY write "
          + "on that store runs");
    }

    /// <summary>
    /// The mixed shape, and the one that shows the damage is not confined to the initialising store.
    /// </summary>
    /// <remarks>
    /// The single-row write happens first and is genuinely inside the boundary; the bulk write then
    /// triggers schema-ensure. Against the unfixed code the DDL committed <b>both</b> — a write that was
    /// correctly enrolled is lost to a later statement's side effect.
    /// </remarks>
    [Fact]
    public async Task An_earlier_write_in_the_same_boundary_is_not_committed_by_a_later_stores_init()
    {
        if (!RequireServer()) return;
        FreshTable();
        var warm = AsyncStore();
        _ = (await warm.ReadAsync(CancellationToken.None)).ToList();   // this one IS initialised
        var cold = AsyncStore();                                        // this one is not

        await using (var uow = SqlUnitOfWork.FromStore(warm))
        {
            await uow.BeginAsync();
            await warm.CreateAsync(new LazyRow { Guid = Guid.NewGuid(), Name = "enrolled", Amount = 1 });
            await cold.CreateAsync(Rows("late-a", "late-b"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "the already-enrolled write must not be committed by another store's lazy schema-ensure");
    }

    [Fact]
    public void A_sync_store_initialising_inside_an_ambient_boundary_still_rolls_back()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();   // deliberately NOT warmed up

        using (var connection = new MySqlConnection(Settings().GetConnectionString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            // The ambient door rather than SetTransactionContext: EnsureInitialized runs in the public
            // wrapper, BEFORE the Core override enters the transaction scope, so the SetTransactionContext
            // door never showed the defect. The ambient travels with the flow and does, which is the shape
            // a sync store used inside an async SqlUnitOfWork actually has.
            using var _ambient = AmbientSqlTransaction.Enter(
                Settings().GetId(), connection, transaction);
            store.Create(Rows("a", "b", "c"));
            transaction.Rollback();
        }

        CommittedCount().Should().Be(0);
    }

    // ---------------------------------------------------------------- what must NOT change

    /// <summary>
    /// Schema is not part of the caller's unit of work: the table stays created when the boundary rolls
    /// back.
    /// </summary>
    /// <remarks>
    /// This is the deliberate consequence of issuing the DDL off the boundary, and it is the behaviour to
    /// want — a rollback that also un-created the table would make the next request pay for it again. It
    /// is stated as a test rather than left implicit because it is the one thing a reader might mistake
    /// for a leak.
    /// </remarks>
    [Fact]
    public async Task The_table_created_by_schema_ensure_survives_the_boundarys_rollback()
    {
        if (!RequireServer()) return;
        NoTable();
        TableExists().Should().BeFalse("the test must force a genuine CREATE, not a no-op");
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        TableExists().Should().BeTrue("schema-ensure is not the caller's unit of work");
        CommittedCount().Should().Be(0, "but the rows it wrote inside the boundary are");
    }

    [Fact]
    public async Task A_committed_boundary_around_a_stores_first_operation_still_persists()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.CommitAsync();
        }

        CommittedCount().Should().Be(3,
            "moving the DDL off the boundary must not take the caller's writes with it");
    }

    [Fact]
    public async Task Without_a_boundary_a_stores_first_operation_behaves_exactly_as_before()
    {
        if (!RequireServer()) return;
        NoTable();
        var store = AsyncStore();

        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);

        TableExists().Should().BeTrue();
        CommittedCount().Should().Be(3);
    }
}
