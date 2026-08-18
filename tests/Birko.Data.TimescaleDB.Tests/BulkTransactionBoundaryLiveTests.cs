using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// The <b>bulk</b> half of the transaction boundary, over a real TimescaleDB <b>hypertable</b>.
///
/// <para>
/// This suite exists because of one claim: <c>TimescaleDBConnector : PostgreSQLConnector</c> overrides no
/// bulk method, so it <i>inherits</i> TASK-242's connector fix — and <i>"it inherits, so it is covered"</i>
/// is exactly the class of claim the escaping-bulk-write defect grew from (TASK-472). Verifying it found
/// two things inheritance did not give:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The store-level publication was missing entirely.</b> TASK-242's own lesson is that joining a boundary
/// is only half of it — something has to <i>publish</i> it, and the layer that publishes is not the layer
/// that joins. The eight provider stores got <c>EnterTransactionScope()</c> in their bulk <c>*Core</c>
/// overrides; TimescaleDB was missed. The connector half was inherited and <b>unreachable from this store</b>,
/// so <c>SetTransactionContext</c> was inert for every bulk write here — and that is the only door a sync
/// store has, since <c>SqlUnitOfWork.FromStore</c> takes an <c>AsyncDataBaseStore</c>.
/// </description></item>
/// <item><description>
/// <b>The table was never a hypertable at all</b>, so this suite could not have meant anything before the
/// identifier fix — see <see cref="HypertableSchemaLiveTests"/>. A "verified over a hypertable" claim
/// against a plain PostgreSQL table is worse than no claim.
/// </description></item>
/// </list>
///
/// <para>
/// <b>Every assertion counts committed rows on a connection of its own.</b> Like PostgreSQL and unlike
/// SQLite, a second connection here is perfectly legal, so an escaping bulk write commits and survives the
/// owner's rollback <i>with no error anywhere</i> — "no exception was thrown" passes against the broken
/// code.
/// </para>
///
/// <para>
/// The rows deliberately span <b>three chunks</b> (<c>chunk_time_interval = 1 day</c>, timestamps a month
/// apart), because chunk routing is the hypertable-specific mechanism inheritance cannot vouch for: a bulk
/// write is fanned out across per-chunk child tables, and the rollback has to take all of them.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_TS_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>), and a
/// skipped run says so out loud — see <see cref="RequireServer"/>.
/// </para>
/// </summary>
public class BulkTransactionBoundaryLiveTests : IDisposable
{
    private const string TableName = "TsBulkTxRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public BulkTransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static TimescaleDBSettings Settings()
        => new(Host!, Database, User, Password, Port, "Ts", "1 day");

    /// <summary>
    /// The time column carries the primary key. That is not a stylistic choice: bulk update and delete key
    /// on <c>Table.GetPrimaryFields()</c> and do nothing at all without one, while TimescaleDB refuses a
    /// unique index that omits the partitioning column — and this framework emits <c>PRIMARY KEY</c> per
    /// column, so a composite <c>(Guid, Ts)</c> key is not expressible. Time-keyed is the only shape where
    /// a Birko hypertable supports the bulk verbs under test.
    /// </summary>
    public class BulkRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public DateTime Ts { get; set; }
    }

    private sealed class BulkRowMapping : IModelMapping<BulkRow>
    {
        public void Configure(ModelMap<BulkRow> map)
        {
            map.ToTable(TableName).HasPrimary(x => x.Ts);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
            map.Property(x => x.Ts);
        }
    }

    // ModelMapRegistry applies into process-wide DataBase state and accumulates, so this type is mapped
    // exactly once per process. Registering one type two ways merges the mappings.
    static BulkTransactionBoundaryLiveTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BulkRowMapping());
        registry.ApplyToDatabase();
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

    /// <summary>
    /// Drops and recreates the hypertable, and <b>asserts it really is one</b> — the premise, re-checked per
    /// test rather than assumed, because every claim in this file is vacuous over a plain table.
    /// </summary>
    private static void FreshHypertable()
    {
        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
        new TimescaleDBConnector(Settings()).CreateTable(new[] { typeof(BulkRow) });

        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM timescaledb_information.hypertables "
                        + $"WHERE hypertable_name = '{TableName}'";
        Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(1,
            "the boundary is being verified OVER A HYPERTABLE — if this is 0 the suite is testing a plain "
          + "PostgreSQL table and proves nothing TimescaleDB-specific");
    }

    private static AsyncTimescaleDBStore<BulkRow> AsyncStore()
    {
        var store = new AsyncTimescaleDBStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static TimescaleDBStore<BulkRow> SyncStore()
    {
        var store = new TimescaleDBStore<BulkRow>();
        store.SetSettings(Settings());
        return store;
    }

    /// <summary>
    /// Rows a month apart, so with a one-day chunk interval each lands in a chunk of its own. Kind is
    /// Unspecified: the mapped column is <c>timestamp without time zone</c> and Npgsql refuses to write a
    /// UTC-kinded DateTime to it.
    /// </summary>
    private static List<BulkRow> Rows(params string[] names)
        => names.Select((n, i) => new BulkRow
        {
            Guid = Guid.NewGuid(),
            Name = n,
            Amount = i + 1,
            Ts = new DateTime(2026, 1 + i, 1, 0, 0, 0, DateTimeKind.Unspecified),
        }).ToList();

    private static int Committed(string? predicate = null)
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{TableName}\""
                        + (predicate == null ? string.Empty : " WHERE " + predicate);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ChunkCount()
    {
        using var conn = new NpgsqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM timescaledb_information.chunks "
                        + $"WHERE hypertable_name = '{TableName}'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ================================================================ async, via SqlUnitOfWork

    [Fact]
    public async Task Async_bulk_create_inside_a_rolled_back_boundary_leaves_nothing()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        Committed().Should().Be(0,
            "the binary COPY must run on the boundary's connection. TimescaleDB inherits PostgreSQL's COPY "
          + "path unchanged, so this is the half inheritance genuinely does deliver — asserted rather than "
          + "assumed");
    }

    [Fact]
    public async Task Async_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        if (!RequireServer()) return;
        FreshHypertable();
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

        Committed("Amount = 999").Should().Be(0);
        Committed().Should().Be(2);
    }

    [Fact]
    public async Task Async_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();
        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.DeleteAsync(loaded, CancellationToken.None);
            await uow.RollbackAsync();
        }

        Committed().Should().Be(3);
    }

    /// <summary>
    /// The hypertable-specific assertion: the three rows live in three different chunks, so the rollback has
    /// to undo a write that was fanned out across separate child tables.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_bulk_write_spanning_several_chunks_leaves_none_of_them()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();

        await store.CreateAsync(Rows("seed-a", "seed-b", "seed-c"), null, CancellationToken.None);
        ChunkCount().Should().BeGreaterThan(1, "the seed rows must actually be spread across chunks");
        var chunksBefore = ChunkCount();

        var later = Enumerable.Range(0, 3).Select(i => new BulkRow
        {
            Guid = Guid.NewGuid(),
            Name = "tx-" + i,
            Amount = 500 + i,
            Ts = new DateTime(2027, 1 + i, 1, 0, 0, 0, DateTimeKind.Unspecified),
        }).ToList();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(later, null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        Committed("Amount >= 500").Should().Be(0,
            "a multi-chunk bulk insert is still one unit of work; before the store published the boundary "
          + "these committed on a second connection and survived the rollback");
        Committed().Should().Be(3);
        ChunkCount().Should().Be(chunksBefore,
            "the chunks created for the rolled-back rows must go with them — a leftover empty chunk would "
          + "mean the DDL half of chunk creation escaped the boundary");
    }

    [Fact]
    public async Task Async_bulk_writes_in_a_committed_boundary_all_persist()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.CommitAsync();
        }

        Committed().Should().Be(3,
            "joining a boundary must not cost the rows their durability — the owner's commit is what makes "
          + "them durable, including for a COPY into a hypertable");
    }

    [Fact]
    public async Task A_bulk_write_and_a_single_write_in_one_boundary_roll_back_together()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new BulkRow
            {
                Guid = Guid.NewGuid(),
                Name = "single",
                Amount = 1,
                Ts = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified),
            });
            await store.CreateAsync(Rows("bulk-a", "bulk-b"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        Committed().Should().Be(0,
            "the mixed operation must be all-or-nothing; the single-row half honoured the boundary from "
          + "TASK-240 onwards while the bulk half did not, which left a service operation HALF applied");
    }

    [Fact]
    public async Task Async_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();

        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
        Committed().Should().Be(3);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();
        foreach (var row in loaded) row.Amount = 42;
        await store.UpdateAsync(loaded, null, CancellationToken.None);
        Committed("Amount = 42").Should().Be(3);

        await store.DeleteAsync(loaded, CancellationToken.None);
        Committed().Should().Be(0);
    }

    // ================================================================ async, via SetTransactionContext

    /// <summary>
    /// The <b>per-store</b> door, which is the one the missing publication actually broke:
    /// <c>SqlUnitOfWork</c> publishes the ambient itself and so worked through the inherited connector even
    /// while the store was silent, whereas <c>SetTransactionContext</c> reaches the connector <i>only</i>
    /// through <c>EnterTransactionScope()</c> in the store's own override.
    /// </summary>
    [Fact]
    public async Task Async_bulk_create_honours_the_per_store_transaction_context()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = AsyncStore();
        _ = await store.ReadAsync(CancellationToken.None);

        await using (var connection = new NpgsqlConnection(Settings().GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
            try
            {
                await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            }
            finally
            {
                store.SetTransactionContext(null);
            }
            await transaction.RollbackAsync();
        }

        Committed().Should().Be(0,
            "SetTransactionContext was inert for every bulk write on this store — TASK-242 wired the "
          + "publication into the eight provider stores and missed TimescaleDB");
    }

    // ================================================================ sync

    /// <summary>
    /// Runs <paramref name="work"/> inside a boundary the caller owns, then rolls it back.
    /// </summary>
    /// <remarks>
    /// The sync store has no unit of work — <c>SqlUnitOfWork.FromStore</c> takes an
    /// <c>AsyncDataBaseStore</c> — so <c>SetTransactionContext</c> is its <b>only</b> door, which is what
    /// made the missing publication total here rather than partial. The store is warmed up first because
    /// <c>EnsureInitialized</c> runs in the public wrapper, before the Core override publishes the boundary;
    /// that ordering is pre-existing and orthogonal to what is under test.
    /// </remarks>
    private static void InRolledBackBoundary(TimescaleDBStore<BulkRow> store, Action work)
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
        FreshHypertable();
        var store = SyncStore();

        InRolledBackBoundary(store, () => store.Create(Rows("a", "b", "c")));

        Committed().Should().Be(0);
    }

    [Fact]
    public void Sync_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = SyncStore();
        store.Create(Rows("a", "b"));

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 999;

        InRolledBackBoundary(store, () => store.Update(loaded));

        Committed("Amount = 999").Should().Be(0);
        Committed().Should().Be(2);
    }

    [Fact]
    public void Sync_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = SyncStore();
        store.Create(Rows("a", "b", "c"));

        var loaded = store.Read().ToList();

        InRolledBackBoundary(store, () => store.Delete(loaded));

        Committed().Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_in_a_committed_boundary_all_persist()
    {
        if (!RequireServer()) return;
        FreshHypertable();
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

        Committed().Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        if (!RequireServer()) return;
        FreshHypertable();
        var store = SyncStore();

        store.Create(Rows("a", "b", "c"));
        Committed().Should().Be(3);

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 42;
        store.Update(loaded);
        Committed("Amount = 42").Should().Be(3);

        store.Delete(loaded);
        Committed().Should().Be(0);
    }
}
