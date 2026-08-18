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
/// The <b>bulk</b> half of the transaction boundary, against a real MySQL.
///
/// <para>
/// TASK-240 wired <see cref="AmbientSqlTransaction"/> into the single-command paths and left every bulk
/// path behind. All six MySQL bulk methods opened their own connection and their own transaction
/// unconditionally, so every collection-shaped repository write — create-many, update-many, delete-many,
/// delete-where, delete-all — happened outside whatever boundary the caller had drawn.
/// </para>
///
/// <para>
/// <b>On MySQL the escape is silent, and that is the whole reason this suite exists.</b> Two connections
/// are perfectly legal here, so the escaping write committed on its own and survived the owner's rollback
/// with no error anywhere: the boundary read as working and was not. Every assertion counts committed
/// rows after a rollback — asserting "no exception was thrown" would pass against the broken code.
/// </para>
///
/// <para>
/// This is the first live MySQL suite in the tree. Gated on <c>BIRKO_MYSQL_HOST</c> (+ <c>_PORT</c> /
/// <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and <b>a skipped run says so out loud</b> — see
/// <see cref="RequireServer"/>: it writes a SKIPPED line to test output, and with
/// <c>BIRKO_REQUIRE_LIVE</c> set it fails instead, so a CI job that is supposed to have a database
/// cannot report green having exercised nothing.
/// </para>
/// </summary>
public class BulkTransactionBoundaryLiveTests : IDisposable
{
    private const string TableName = "BulkTxRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public BulkTransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

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

    public class BulkRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class BulkRowMapping : IModelMapping<BulkRow>
    {
        public void Configure(ModelMap<BulkRow> map)
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

    private static void FreshTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BulkRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(BulkRow) });
    }

    private static AsyncMySQLStore<BulkRow> AsyncStore()
    {
        var store = new AsyncMySQLStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static MySQLStore<BulkRow> SyncStore()
    {
        var store = new MySQLStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static List<BulkRow> Rows(params string[] names)
        => names.Select((n, i) => new BulkRow { Guid = Guid.NewGuid(), Name = n, Amount = i + 1 }).ToList();

    /// <summary>
    /// Counts on a connection of its own, so the answer is what is <b>committed</b> — never what some
    /// still-open transaction can see.
    /// </summary>
    private static int CommittedCount(string? predicate = null)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{TableName}`"
                        + (predicate == null ? string.Empty : $" WHERE {predicate}");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ================================================================ async bulk

    /// <summary>
    /// Forces the store's lazy schema-ensure to happen <b>before</b> a boundary is opened.
    /// </summary>
    /// <remarks>
    /// Not tidiness — on MySQL it is the difference between testing the boundary and testing nothing.
    /// <c>EnsureInitializedAsync</c> runs in the public wrapper and issues <c>CREATE TABLE</c> through the
    /// ambient connection, and <b>MySQL implicitly commits an open transaction on any DDL</b>. A store
    /// whose first ever operation happens inside a boundary therefore commits that boundary before its own
    /// write even runs, and the subsequent rollback undoes nothing. Measured: without this warm-up the
    /// bulk-create test reported 3 surviving rows against the FIXED connector.
    /// <para>
    /// That is a real hazard for a consumer, not only for this suite — filed separately; it is orthogonal
    /// to whether a bulk write joins a boundary, which is what these tests exist to pin.
    /// </para>
    /// </remarks>
    private static async Task WarmUpAsync(AsyncMySQLStore<BulkRow> store)
        => _ = (await store.ReadAsync(CancellationToken.None)).ToList();

    [Fact]
    public async Task Async_bulk_create_inside_a_rolled_back_boundary_leaves_nothing()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await WarmUpAsync(store);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "against the unfixed connector the batched multi-row INSERT ran on a second connection, "
          + "committed, and survived this rollback with no error at all");
    }

    [Fact]
    public async Task Async_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await store.CreateAsync(Rows("a", "b"), null, CancellationToken.None);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();
        foreach (var row in loaded) row.Amount = 999;

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.UpdateAsync(loaded, null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount("Amount = 999").Should().Be(0);
        CommittedCount().Should().Be(2);
    }

    [Fact]
    public async Task Async_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.DeleteAsync(loaded, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(3);
    }

    [Fact]
    public async Task Async_bulk_writes_in_a_committed_boundary_all_persist()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await WarmUpAsync(store);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.CommitAsync();
        }

        CommittedCount().Should().Be(3,
            "joining a boundary must not cost the rows their durability — the owner's commit is what makes "
          + "them durable");
    }

    /// <summary>
    /// A bulk write and a single-row write in one boundary are one unit.
    /// </summary>
    /// <remarks>
    /// This is the consumer shape that broke (Symbio TASK-442): after TASK-240 the single-row half honoured
    /// the boundary and the bulk half did not, so a rollback left a service operation <i>half</i> applied —
    /// worse than either half being wrong on its own.
    /// </remarks>
    [Fact]
    public async Task A_bulk_write_and_a_single_write_in_one_boundary_roll_back_together()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();
        await WarmUpAsync(store);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new BulkRow { Guid = Guid.NewGuid(), Name = "single", Amount = 1 });
            await store.CreateAsync(Rows("bulk-a", "bulk-b"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "before the fix the single row vanished and the two bulk rows stayed");
    }

    [Fact]
    public async Task Async_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
        CommittedCount().Should().Be(3);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();
        foreach (var row in loaded) row.Amount = 42;
        await store.UpdateAsync(loaded, null, CancellationToken.None);
        CommittedCount("Amount = 42").Should().Be(3);

        await store.DeleteAsync(loaded, CancellationToken.None);
        CommittedCount().Should().Be(0);
    }

    // ================================================================ sync bulk

    /// <summary>
    /// Runs <paramref name="work"/> inside a boundary the caller owns, then rolls it back.
    /// </summary>
    /// <remarks>
    /// The sync store has no unit of work — its door is <c>SetTransactionContext</c> +
    /// <c>DataBaseStore.EnterTransactionScope</c>. The store is warmed up first because
    /// <c>EnsureInitialized</c> runs in the public wrapper, before the Core override publishes the
    /// boundary; that is pre-existing and orthogonal to what is under test.
    /// </remarks>
    private static void InRolledBackBoundary(MySQLStore<BulkRow> store, Action work)
    {
        _ = store.Read().ToList();

        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        try
        {
            work();
        }
        finally
        {
            store.SetTransactionContext(null);
        }
        transaction.Rollback();
    }

    [Fact]
    public void Sync_bulk_create_inside_a_rolled_back_boundary_leaves_nothing()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();

        InRolledBackBoundary(store, () => store.Create(Rows("a", "b", "c")));

        CommittedCount().Should().Be(0);
    }

    [Fact]
    public void Sync_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();
        store.Create(Rows("a", "b"));

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 999;

        InRolledBackBoundary(store, () => store.Update(loaded));

        CommittedCount("Amount = 999").Should().Be(0);
        CommittedCount().Should().Be(2);
    }

    [Fact]
    public void Sync_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();
        store.Create(Rows("a", "b", "c"));

        var loaded = store.Read().ToList();

        InRolledBackBoundary(store, () => store.Delete(loaded));

        CommittedCount().Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_in_a_committed_boundary_all_persist()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();
        _ = store.Read().ToList();

        using (var connection = new MySqlConnection(Settings().GetConnectionString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
            try
            {
                store.Create(Rows("a", "b", "c"));
            }
            finally
            {
                store.SetTransactionContext(null);
            }
            transaction.Commit();
        }

        CommittedCount().Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = SyncStore();

        store.Create(Rows("a", "b", "c"));
        CommittedCount().Should().Be(3);

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 42;
        store.Update(loaded);
        CommittedCount("Amount = 42").Should().Be(3);

        store.Delete(loaded);
        CommittedCount().Should().Be(0);
    }
}
