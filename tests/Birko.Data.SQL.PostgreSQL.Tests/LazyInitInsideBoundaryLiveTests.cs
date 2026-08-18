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
/// TASK-243, the contract pin for a provider the fix must <b>not</b> disturb.
///
/// <para>
/// A store initialises lazily, so its first data access issues <c>CREATE TABLE IF NOT EXISTS</c> from
/// inside the public CRUD wrapper — inside the caller's boundary, if one is open. On MySQL that silently
/// committed the boundary, which is what TASK-243 fixed by routing schema DDL off it (see
/// <see cref="AbstractConnectorBase.SupportsTransactionalDdl"/>). PostgreSQL has transactional DDL and
/// never had the defect, so <b>every rollback assertion here passed before the fix and must keep passing
/// after it</b>.
/// </para>
///
/// <para>
/// That is the point of the file. The fix is a switch on a provider capability, and the way such a switch
/// goes wrong is by being flipped for a provider that did not need it — so the provider that did not need
/// it gets the same assertions, and a regression surfaces as a red test rather than as nobody looking.
/// </para>
///
/// <para>
/// One assertion is deliberately the <b>opposite</b> of the MySQL suite's: a table genuinely created by
/// schema-ensure inside a boundary is rolled back with it here and survives there. Both are correct,
/// because the providers differ — pinning both is what stops someone unifying them later by reasoning
/// from symmetry.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and a
/// skipped run says so out loud — see <see cref="RequireServer"/>.
/// </para>
/// </summary>
public class LazyInitInsideBoundaryLiveTests : IDisposable
{
    private const string TableName = "LazyInitRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public LazyInitInsideBoundaryLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>Creates the table through a connector of its own, leaving every store uninitialised.</summary>
    private static void FreshTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new LazyRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(LazyRow) });
    }

    /// <summary>Drops the table so the lazy schema-ensure has to genuinely create it.</summary>
    private static void NoTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new LazyRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
    }

    private static AsyncPostgreSQLStore<LazyRow> AsyncStore()
    {
        var store = new AsyncPostgreSQLStore<LazyRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static PostgreSQLStore<LazyRow> SyncStore()
    {
        var store = new PostgreSQLStore<LazyRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static List<LazyRow> Rows(params string[] names)
        => names.Select((n, i) => new LazyRow { Guid = Guid.NewGuid(), Name = n, Amount = i + 1 }).ToList();

    private static int CommittedCount()
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{TableName}\"";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool TableExists()
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables "
                        + "WHERE table_schema = current_schema() AND table_name = @t";
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
            "PostgreSQL has transactional DDL, so this always worked — it is pinned so that routing "
          + "schema DDL off the boundary for MySQL cannot silently change it here");
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
            "the single-row path is pinned for the same reason as the bulk one");
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

        using (var connection = new NpgsqlConnection(Settings().GetConnectionString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            // The ambient door rather than SetTransactionContext, mirroring the MySQL suite: it is the
            // door a sync store used inside an async SqlUnitOfWork flow actually goes through.
            using var _ambient = AmbientSqlTransaction.Enter(
                Settings().GetId(), connection, transaction);
            store.Create(Rows("a", "b", "c"));
            transaction.Rollback();
        }

        CommittedCount().Should().Be(0);
    }

    // ---------------------------------------------------------------- what must NOT change

    /// <summary>
    /// On PostgreSQL a table genuinely created by schema-ensure inside a boundary is rolled back
    /// <b>with</b> it — the exact opposite of the MySQL pin, and correct on both.
    /// </summary>
    /// <remarks>
    /// PostgreSQL's DDL is transactional, so the <c>CREATE TABLE</c> joins the caller's transaction and
    /// dies with it; MySQL's is not, so the same DDL is issued off the boundary and survives. Neither
    /// loses data — the next operation re-runs schema-ensure — but the store is left believing it is
    /// initialised, which is the sharp edge TASK-244 owns.
    /// </remarks>
    [Fact]
    public async Task A_table_created_by_schema_ensure_is_rolled_back_with_the_boundary()
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

        TableExists().Should().BeFalse(
            "PostgreSQL DDL is transactional, so the table went with the rollback — stated explicitly "
          + "because it is the opposite of the MySQL pin and both are intended");
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
