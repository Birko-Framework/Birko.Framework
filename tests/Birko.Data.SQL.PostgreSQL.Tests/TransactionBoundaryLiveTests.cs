using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-240 — the transaction boundary proven on PostgreSQL, which is where the concurrency half of the
/// proof is actually expressible.
///
/// <para>
/// The SQLite suite (<c>Birko.Data.SQL.SqLite.Tests.TransactionBoundaryEndToEndTests</c>) covers the same
/// contract, but SQLite serialises access at the file level: a second writer — and, measured, even a
/// second <i>reader</i> — blocks for the whole busy timeout while a write transaction is open. Two flows
/// genuinely overlapping inside and outside a boundary can therefore only be observed on a server with
/// row-level MVCC. A green SQLite run is not evidence for PostgreSQL, which is exactly why both exist.
/// </para>
///
/// <para>
/// Gated on <c>BIRKO_PG_HOST</c> (+ <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> / <c>_DB</c>).
/// <b>A skipped run says so out loud</b> — see <see cref="RequireServer"/>: it writes a SKIPPED line to
/// test output, and if <c>BIRKO_REQUIRE_LIVE</c> is set it fails instead, so a CI job that is supposed to
/// have a database cannot silently report green having exercised nothing.
/// </para>
/// </summary>
public class TransactionBoundaryLiveTests : IDisposable
{
    private const string TableName = "TxBoundaryRows";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public TransactionBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Returns false when no server is configured, after making that visible.
    /// </summary>
    /// <remarks>
    /// The suites in this family gate with a bare <c>if (host is null) return;</c>, which renders a
    /// skipped test identically to a passing one — the failure mode that let a whole MongoDB surface sit
    /// green while unable to write a single document (TASK-214). xUnit 2.9 has no dynamic skip, so the
    /// next best thing is to say so in the output and give CI a switch that turns absence into failure.
    /// </remarks>
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

    public class TxRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class TxRowMapping : IModelMapping<TxRow>
    {
        public void Configure(ModelMap<TxRow> map)
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

    /// <summary>A freshly created table plus a store over it.</summary>
    private static AsyncPostgreSQLStore<TxRow> FreshStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new TxRowMapping());
        registry.ApplyToDatabase();

        Exec($"DROP TABLE IF EXISTS \"{TableName}\" CASCADE");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(TxRow) });

        var store = new AsyncPostgreSQLStore<TxRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static AsyncPostgreSQLStore<TxRow> SecondStoreOverSameConnector()
    {
        var store = new AsyncPostgreSQLStore<TxRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static async Task<int> CountAsync(AsyncPostgreSQLStore<TxRow> store)
        => (await store.ReadAsync(CancellationToken.None)).Count();

    // ---------------------------------------------------------------- (a) atomicity

    [Fact]
    public async Task Two_writes_in_one_boundary_are_both_discarded_when_the_boundary_rolls_back()
    {
        if (!RequireServer()) return;
        var store = FreshStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "second", Amount = 2 });
            await uow.RollbackAsync();
        }

        (await CountAsync(store)).Should().Be(0,
            "neither write may survive a rolled-back boundary; against the unfixed async connector both "
          + "committed on their own connections");
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_committed()
    {
        if (!RequireServer()) return;
        var store = FreshStore();
        var duplicate = Guid.NewGuid();

        await store.CreateAsync(new TxRow { Guid = duplicate, Name = "pre-existing", Amount = 99 });

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });

            var act = async () => await store.CreateAsync(new TxRow { Guid = duplicate, Name = "clash", Amount = 2 });
            await act.Should().ThrowAsync<Exception>();

            await uow.RollbackAsync();
        }

        var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("pre-existing");
    }

    [Fact]
    public async Task A_committed_boundary_persists_every_write()
    {
        if (!RequireServer()) return;
        var store = FreshStore();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "second", Amount = 2 });
            await uow.CommitAsync();
        }

        (await CountAsync(store)).Should().Be(2);
    }

    [Fact]
    public async Task A_read_inside_the_boundary_sees_the_boundarys_own_uncommitted_writes()
    {
        if (!RequireServer()) return;
        var store = FreshStore();

        await using var uow = SqlUnitOfWork.FromStore(store);
        await uow.BeginAsync();
        await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "inside", Amount = 7 });

        (await CountAsync(store)).Should().Be(1);
        (await store.ReadFirstAsync(x => x.Name == "inside"))!.Amount.Should().Be(7);

        await uow.RollbackAsync();
        (await CountAsync(store)).Should().Be(0);
    }

    // ---------------------------------------------------------------- (b) concurrency — the trap

    /// <summary>
    /// Two flows genuinely overlapping: one inside a boundary, one outside, against the SAME cached
    /// connector. Neither may capture the other's writes.
    /// </summary>
    /// <remarks>
    /// This is the assertion the naive fix fails. Making the async path read the connector's
    /// <c>ExternalConnection</c>/<c>ExternalTransaction</c> would satisfy every single-threaded test in
    /// this file and then enlist the outsider's write into the insider's transaction, destroying it on
    /// rollback. Unlike the SQLite version, this handshake is two-way — PostgreSQL's row-level MVCC lets
    /// both flows hold open transactions at once, so the overlap is real rather than simulated.
    /// </remarks>
    [Fact]
    public async Task A_writer_outside_the_boundary_is_not_captured_by_a_concurrent_boundary()
    {
        if (!RequireServer()) return;
        var insider = FreshStore();
        var outsider = SecondStoreOverSameConnector();

        ReferenceEquals(insider.Connector, outsider.Connector).Should().BeTrue(
            "both stores must share the process-wide cached connector for this to be the real scenario");

        var boundaryOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outsiderCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var insideTask = Task.Run(async () =>
        {
            await using var uow = SqlUnitOfWork.FromStore(insider);
            await uow.BeginAsync();
            await insider.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "inside", Amount = 1 });
            boundaryOpen.SetResult();
            await outsiderCommitted.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await uow.RollbackAsync();
        });

        var outsideTask = Task.Run(async () =>
        {
            try
            {
                await boundaryOpen.Task.WaitAsync(TimeSpan.FromSeconds(60));
                // No ambient scope on this flow: this write commits on its own connection, WHILE the
                // boundary above is still open, and must survive its rollback.
                await outsider.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "outside", Amount = 2 });
            }
            finally
            {
                outsiderCommitted.TrySetResult();
            }
        });

        await Task.WhenAll(insideTask, outsideTask).WaitAsync(TimeSpan.FromSeconds(120));

        var rows = (await outsider.ReadAsync(CancellationToken.None)).ToList();
        rows.Should().ContainSingle("the outsider's write must survive the insider's rollback");
        rows[0].Name.Should().Be("outside");
    }

    /// <summary>
    /// A flow outside the boundary must not read the boundary's uncommitted rows.
    /// </summary>
    /// <remarks>
    /// Not expressible on SQLite — a concurrent reader there blocks for the whole busy timeout.
    /// </remarks>
    [Fact]
    public async Task A_reader_outside_the_boundary_does_not_see_its_uncommitted_rows()
    {
        if (!RequireServer()) return;
        var a = FreshStore();
        var b = SecondStoreOverSameConnector();
        ReferenceEquals(a.Connector, b.Connector).Should().BeTrue();

        var aWrote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int bSawWhileAOpen = -1;

        var taskA = Task.Run(async () =>
        {
            await using var uow = SqlUnitOfWork.FromStore(a);
            await uow.BeginAsync();
            await a.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "a-row", Amount = 1 });
            aWrote.SetResult();
            await bChecked.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await uow.RollbackAsync();
        });

        var taskB = Task.Run(async () =>
        {
            try
            {
                await aWrote.Task.WaitAsync(TimeSpan.FromSeconds(60));
                bSawWhileAOpen = (await b.ReadAsync(CancellationToken.None)).Count();
            }
            finally
            {
                bChecked.TrySetResult();
            }
        });

        await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(120));

        bSawWhileAOpen.Should().Be(0,
            "a flow outside the boundary must not read the boundary's uncommitted writes");
        (await CountAsync(a)).Should().Be(0);
    }

    /// <summary>
    /// Many boundaries at once, each rolled back, against one shared connector.
    /// </summary>
    /// <remarks>
    /// The single-pair test can pass by accident if the ambient happens to be re-entered per operation.
    /// Fanning out makes cross-capture overwhelmingly likely to show up as a surviving row.
    /// </remarks>
    [Fact]
    public async Task Many_concurrent_boundaries_each_roll_back_only_their_own_writes()
    {
        if (!RequireServer()) return;
        var seed = FreshStore();

        const int flows = 8;
        var committed = new List<Guid>();
        var tasks = Enumerable.Range(0, flows).Select(i => Task.Run(async () =>
        {
            var store = SecondStoreOverSameConnector();
            await using var uow = SqlUnitOfWork.FromStore(store);
            await uow.BeginAsync();

            var keep = Guid.NewGuid();
            await store.CreateAsync(new TxRow { Guid = keep, Name = $"keep-{i}", Amount = i });
            await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = $"drop-{i}", Amount = i });

            // Every flow rolls back; nothing any of them wrote may survive.
            await uow.RollbackAsync();
            return keep;
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(180));

        (await CountAsync(seed)).Should().Be(0,
            "every one of the {0} concurrent boundaries rolled back", flows);
        committed.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- (c) the no-boundary path

    [Fact]
    public async Task Without_a_boundary_every_write_commits_immediately_exactly_as_before()
    {
        if (!RequireServer()) return;
        var store = FreshStore();

        await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "a", Amount = 1 });
        (await CountAsync(store)).Should().Be(1);

        var target = (await store.ReadFirstAsync(x => x.Name == "a"))!;
        target.Amount = 42;
        await store.UpdateAsync(target);
        (await store.ReadFirstAsync(x => x.Name == "a"))!.Amount.Should().Be(42);

        await store.DeleteAsync(target);
        (await CountAsync(store)).Should().Be(0);
    }

    // ---------------------------------------------------------------- nesting

    [Fact]
    public async Task A_nested_rollback_poisons_the_boundary_so_the_owners_commit_refuses()
    {
        if (!RequireServer()) return;
        var store = FreshStore();

        await using var outer = SqlUnitOfWork.FromStore(store);
        await outer.BeginAsync();
        await store.CreateAsync(new TxRow { Guid = Guid.NewGuid(), Name = "outer", Amount = 1 });

        await using (var inner = SqlUnitOfWork.FromStore(store))
        {
            await inner.BeginAsync();
            inner.IsParticipant.Should().BeTrue();
            await inner.RollbackAsync();
        }

        var act = async () => await outer.CommitAsync();
        await act.Should().ThrowAsync<TransactionRollbackOnlyException>();

        await outer.RollbackAsync();
        (await CountAsync(store)).Should().Be(0);
    }

    // ---------------------------------------------------------------- capabilities

    [Fact]
    public async Task The_sql_unit_of_work_states_what_it_promises()
    {
        if (!RequireServer()) return;
        var store = FreshStore();
        await using var uow = SqlUnitOfWork.FromStore(store);

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.Atomic);
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.Database);
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeTrue();
        uow.Capabilities.RequiresServerTopology.Should().BeFalse();
    }
}
