using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// The <b>bulk</b> half of the transaction boundary, against a real PostgreSQL.
///
/// <para>
/// TASK-240 wired <see cref="AmbientSqlTransaction"/> into the single-command paths and left every bulk
/// path behind: <c>BulkInsert</c> / <c>BulkUpdate</c> / <c>BulkDelete</c> and their async twins opened
/// their own connection and their own transaction unconditionally.
/// </para>
///
/// <para>
/// <b>PostgreSQL is where the defect is silent, which is why it needs its own live proof.</b> On SQLite
/// the escaping write blocks on a lock it cannot take and fails loudly. Here two connections are
/// perfectly legal, so the bulk write simply committed on its own and <i>survived the owner's rollback
/// with no error anywhere</i> — a rolled-back operation quietly leaving its bulk writes behind. Every
/// assertion below therefore counts committed rows after a rollback; asserting "no exception was thrown"
/// would pass against the broken code.
/// </para>
///
/// <para>
/// <c>BulkInsert</c> is the odd one out and gets its own coverage: it is a binary <c>COPY … FROM STDIN
/// (FORMAT BINARY)</c> with no transaction of its own at all, so participating means running the COPY on
/// the boundary's connection rather than gating a commit.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and a
/// skipped run says so out loud — see <see cref="RequireServer"/>.
/// </para>
/// </summary>
public class BulkTransactionBoundaryLiveTests : IDisposable
{
    private const string TableName = "BulkTxRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public BulkTransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

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
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE"); } catch { }
    }

    private static void FreshTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BulkRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(BulkRow) });
    }

    private static AsyncPostgreSQLStore<BulkRow> AsyncStore()
    {
        var store = new AsyncPostgreSQLStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static PostgreSQLStore<BulkRow> SyncStore()
    {
        var store = new PostgreSQLStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static List<BulkRow> Rows(params string[] names)
        => names.Select((n, i) => new BulkRow { Guid = Guid.NewGuid(), Name = n, Amount = i + 1 }).ToList();

    /// <summary>
    /// Counts on a connection of its own, so the answer is what is <b>committed</b> — never what some
    /// still-open transaction can see.
    /// </summary>
    private static int CommittedCount()
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{TableName}\"";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Counts committed rows matching <paramref name="predicate"/>. The predicate names its column
    /// <b>bare</b>: the base-table DDL emits column definitions unquoted, so PostgreSQL stores them
    /// case-folded and a quoted "Amount" resolves to nothing (42703).
    /// </summary>
    private static int CommittedCountWhere(string predicate)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{TableName}\" WHERE {predicate}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ================================================================ async bulk

    [Fact]
    public async Task Async_bulk_create_inside_a_rolled_back_boundary_leaves_nothing()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "the binary COPY must run on the boundary's connection; against the unfixed connector it ran on "
          + "a second one, committed, and survived this rollback with no error at all");
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

        CommittedCountWhere("Amount = 999").Should().Be(0);
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

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.CommitAsync();
        }

        CommittedCount().Should().Be(3,
            "joining a boundary must not cost the rows their durability — the owner's commit is what makes "
          + "them durable, including for a COPY");
    }

    /// <summary>
    /// A bulk write and a single-row write in one boundary are one unit.
    /// </summary>
    /// <remarks>
    /// The mixed case is the consumer shape that broke (Symbio TASK-442): the single-row half already
    /// honoured the boundary after TASK-240 while the bulk half did not, so a rollback left a service
    /// operation <i>half</i> applied — worse than either half being wrong on its own.
    /// </remarks>
    [Fact]
    public async Task A_bulk_write_and_a_single_write_in_one_boundary_roll_back_together()
    {
        if (!RequireServer()) return;
        FreshTable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new BulkRow { Guid = Guid.NewGuid(), Name = "single", Amount = 1 });
            await store.CreateAsync(Rows("bulk-a", "bulk-b"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount().Should().Be(0,
            "the mixed operation must be all-or-nothing; before the fix the single row vanished and the two "
          + "bulk rows stayed");
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
        CommittedCountWhere("Amount = 42").Should().Be(3);

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
    /// boundary; that is pre-existing and orthogonal to what is under test here.
    /// </remarks>
    private static void InRolledBackBoundary(PostgreSQLStore<BulkRow> store, Action work)
    {
        _ = store.Read().ToList();

        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
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

        CommittedCountWhere("Amount = 999").Should().Be(0);
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

        using (var connection = new NpgsqlConnection(Settings().GetConnectionString()))
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
        CommittedCountWhere("Amount = 42").Should().Be(3);

        store.Delete(loaded);
        CommittedCount().Should().Be(0);
    }
}
