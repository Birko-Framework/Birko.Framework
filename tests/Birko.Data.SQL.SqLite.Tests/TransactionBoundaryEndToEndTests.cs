using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;

using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-240 — end-to-end proof against a real on-disk SQLite database that an <b>async</b> write actually
/// participates in a transaction boundary.
///
/// <para>Before this, <c>AbstractAsyncConnector</c> inherited <c>ExternalConnection</c>/
/// <c>ExternalTransaction</c> from its sync base and never read them: every async entry point did
/// <c>await using var db = CreateConnection(_settings)</c> and, in the transactional case, opened its own
/// <c>BeginTransactionAsync</c> per statement batch. A caller could set a transaction, get no error, and
/// have every write commit outside it.</para>
///
/// <para><b>These tests count rows rather than trust return values.</b> The defect's whole character was
/// that it reported success — a suite that asserted "no exception was thrown" would have passed against
/// the broken code.</para>
/// </summary>
public class TransactionBoundaryEndToEndTests : IDisposable
{
    private readonly string _root;

    public TransactionBoundaryEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-tx-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("TxRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private static int _seq;

    /// <summary>
    /// A store over its own database file. Each test gets a distinct file so the process-wide connector
    /// cache in <c>DataBase.GetConnector</c> hands out a distinct connector per test.
    /// </summary>
    private (AsyncSQLiteStore<Row> Store, SqLiteSettings Settings) NewStore(string? dbName = null)
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();
        var name = dbName ?? $"tx{Interlocked.Increment(ref _seq)}.db";
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = name });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });
        var settings = new SqLiteSettings(_root, name);
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(settings);
        return (store, settings);
    }

    private static SqlUnitOfWork NewUnitOfWork(AsyncSQLiteStore<Row> store)
        => SqlUnitOfWork.FromStore(store);

    private static async Task<int> CountAsync(AsyncSQLiteStore<Row> store)
        => (await store.ReadAsync(CancellationToken.None)).Count();

    // ---------------------------------------------------------------- (a) atomicity

    [Fact]
    public async Task Two_writes_in_one_boundary_are_both_discarded_when_the_boundary_rolls_back()
    {
        var (store, _) = NewStore();

        await using (var uow = NewUnitOfWork(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "second", Amount = 2 });
            await uow.RollbackAsync();
        }

        // Read the rows back. Against the unfixed connector both writes committed on their own
        // connections and this is 2.
        (await CountAsync(store)).Should().Be(0,
            "neither write may survive a rolled-back boundary");
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_committed()
    {
        var (store, _) = NewStore();
        var duplicate = Guid.NewGuid();

        await store.CreateAsync(new Row { Guid = duplicate, Name = "pre-existing", Amount = 99 });
        (await CountAsync(store)).Should().Be(1, "seed");

        var uow = NewUnitOfWork(store);
        await using (uow)
        {
            await uow.BeginAsync();
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });

            // Second write violates the unique primary key and throws.
            var act = async () => await store.CreateAsync(new Row { Guid = duplicate, Name = "clash", Amount = 2 });
            await act.Should().ThrowAsync<Exception>();

            await uow.RollbackAsync();
        }

        var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
        rows.Should().ContainSingle("only the pre-existing row may remain");
        rows[0].Name.Should().Be("pre-existing");
    }

    [Fact]
    public async Task A_committed_boundary_persists_every_write()
    {
        var (store, _) = NewStore();

        await using (var uow = NewUnitOfWork(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "first", Amount = 1 });
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "second", Amount = 2 });
            await uow.CommitAsync();
        }

        (await CountAsync(store)).Should().Be(2);
    }

    // ---------------------------------------------------------------- read-your-own-writes

    [Fact]
    public async Task A_read_inside_the_boundary_sees_the_boundarys_own_uncommitted_writes()
    {
        var (store, _) = NewStore();

        await using var uow = NewUnitOfWork(store);
        await uow.BeginAsync();
        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "inside", Amount = 7 });

        // Read-then-write is the shape of every service method this exists for. A read that escaped the
        // boundary would return the pre-transaction snapshot — a wrong answer, not a missing feature.
        (await CountAsync(store)).Should().Be(1);
        (await store.ReadFirstAsync(x => x.Name == "inside"))!.Amount.Should().Be(7);

        await uow.RollbackAsync();
        (await CountAsync(store)).Should().Be(0);
    }

    // ---------------------------------------------------------------- (b) concurrency — the trap

    /// <summary>
    /// The assertion that separates a real fix from the naive one.
    /// </summary>
    /// <remarks>
    /// Connectors are cached process-wide per (type, settings id), so the two stores below share ONE
    /// connector object. Had the fix been "make the async path read <c>ExternalConnection</c>", the
    /// outsider's write would have been captured by the insider's transaction and would have vanished
    /// when it rolled back. A single-threaded test cannot see that.
    /// <para>
    /// The handshake is deliberately ONE-WAY. SQLite serialises writers at the file level, so the
    /// outsider's INSERT blocks until the boundary ends — a two-way handshake (insider waits for the
    /// outsider to finish) deadlocks, which is measurably what happens: the outsider's write sat on the
    /// writer lock for the full timeout and only completed once the boundary was disposed. That is a
    /// property of SQLite, not of the boundary, and it is why the genuinely concurrent version of this
    /// proof lives in the PostgreSQL suite.
    /// </para>
    /// <para>
    /// The assertion still discriminates: had the ambient leaked into the outsider's flow, its write
    /// would have joined the boundary (no blocking at all) and been rolled back with it, leaving zero
    /// rows instead of one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_writer_outside_the_boundary_is_not_captured_by_a_concurrent_boundary()
    {
        var (insider, _) = NewStore("shared.db");
        var outsider = new AsyncSQLiteStore<Row>();
        outsider.SetSettings(new SqLiteSettings(_root, "shared.db"));

        // Same cached connector object — otherwise this test proves nothing about the trap.
        ReferenceEquals(insider.Connector, outsider.Connector).Should().BeTrue(
            "both stores must share the process-wide cached connector for this to be the real scenario");

        var boundaryOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outsiderStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var insideTask = Task.Run(async () =>
        {
            await using var uow = NewUnitOfWork(insider);
            await uow.BeginAsync();
            await insider.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "inside", Amount = 1 });
            boundaryOpen.SetResult();
            await outsiderStarting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await uow.RollbackAsync();
        });

        var outsideTask = Task.Run(async () =>
        {
            await boundaryOpen.Task.WaitAsync(TimeSpan.FromSeconds(30));
            outsiderStarting.SetResult();
            // No ambient scope on this flow: this write must commit on its own connection and survive.
            await outsider.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "outside", Amount = 2 });
        });

        await Task.WhenAll(insideTask, outsideTask).WaitAsync(TimeSpan.FromSeconds(60));

        var rows = (await outsider.ReadAsync(CancellationToken.None)).ToList();
        rows.Should().ContainSingle("the outsider's write must survive the insider's rollback");
        rows[0].Name.Should().Be("outside");
    }

    // A concurrent READER outside the boundary is NOT provable here, and the attempt is recorded rather
    // than quietly dropped: Microsoft.Data.Sqlite blocks a reader on another connection for the whole
    // busy timeout while a write transaction is open (measured: the read sat for 30s and then timed out).
    // That is a property of SQLite's file locking, not of the boundary. The reader-isolation proof — and
    // the genuinely simultaneous writer-vs-writer proof — therefore live in
    // Birko.Data.SQL.PostgreSQL.Tests.TransactionBoundaryLiveTests, which is the point of proving this on
    // both engines rather than trusting a green SQLite run.

    // ---------------------------------------------------------------- (c) the no-boundary path

    [Fact]
    public async Task Without_a_boundary_every_write_commits_immediately_exactly_as_before()
    {
        var (store, _) = NewStore();

        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "a", Amount = 1 });
        (await CountAsync(store)).Should().Be(1, "a write with no boundary commits on its own");

        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "b", Amount = 2 });
        (await CountAsync(store)).Should().Be(2);

        var target = (await store.ReadFirstAsync(x => x.Name == "a"))!;
        target.Amount = 42;
        await store.UpdateAsync(target);
        (await store.ReadFirstAsync(x => x.Name == "a"))!.Amount.Should().Be(42);

        await store.DeleteAsync(target);
        (await CountAsync(store)).Should().Be(1);
    }

    [Fact]
    public async Task The_ambient_scope_is_empty_once_the_boundary_has_ended()
    {
        var (store, _) = NewStore();

        await using (var uow = NewUnitOfWork(store))
        {
            await uow.BeginAsync();
            AmbientSqlTransaction.Current.Should().NotBeNull("inside the boundary");
            await uow.CommitAsync();
        }

        AmbientSqlTransaction.Current.Should().BeNull(
            "a leaked scope would silently enlist every later write in a disposed transaction");
    }

    // ---------------------------------------------------------------- scoping by database

    [Fact]
    public async Task A_boundary_on_one_database_does_not_capture_writes_to_another()
    {
        var (dbA, _) = NewStore("scope-a.db");
        var (dbB, _) = NewStore("scope-b.db");

        await using (var uow = NewUnitOfWork(dbA))
        {
            await uow.BeginAsync();
            await dbA.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "in-a", Amount = 1 });
            // Same flow, different database: this must NOT join A's transaction.
            await dbB.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "in-b", Amount = 2 });
            await uow.RollbackAsync();
        }

        (await CountAsync(dbA)).Should().Be(0, "A's write was inside the rolled-back boundary");
        (await CountAsync(dbB)).Should().Be(1,
            "B's write was against a different database and must be unaffected — the ambient entry is "
          + "keyed by settings id precisely so this cannot bleed");
    }

    // ---------------------------------------------------------------- nesting

    [Fact]
    public async Task A_nested_unit_of_work_joins_the_enclosing_boundary_instead_of_opening_its_own()
    {
        var (store, _) = NewStore();

        await using var outer = NewUnitOfWork(store);
        await outer.BeginAsync();
        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "outer", Amount = 1 });

        await using (var inner = NewUnitOfWork(store))
        {
            await inner.BeginAsync();
            inner.IsParticipant.Should().BeTrue("nesting must join, not open a second transaction");
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "inner", Amount = 2 });
            await inner.CommitAsync();
        }

        // The inner "commit" must not have committed anything on its own.
        await outer.RollbackAsync();
        (await CountAsync(store)).Should().Be(0,
            "an inner commit inside an outer rollback is partial application reporting green");
    }

    [Fact]
    public async Task A_nested_rollback_poisons_the_boundary_so_the_owners_commit_refuses()
    {
        var (store, _) = NewStore();

        await using var outer = NewUnitOfWork(store);
        await outer.BeginAsync();
        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "outer", Amount = 1 });

        await using (var inner = NewUnitOfWork(store))
        {
            await inner.BeginAsync();
            await inner.RollbackAsync();
        }

        var act = async () => await outer.CommitAsync();
        await act.Should().ThrowAsync<TransactionRollbackOnlyException>(
            "committing over a participant's rollback would discard its decision silently");

        await outer.RollbackAsync();
        (await CountAsync(store)).Should().Be(0);
    }

    // ---------------------------------------------------------------- the per-store door

    [Fact]
    public async Task SetTransactionContext_is_honoured_rather_than_accepted_and_dropped()
    {
        var (store, _) = NewStore();

        await using var uow = NewUnitOfWork(store);
        await uow.BeginAsync();

        // The per-store door, matching Mongo/Raven/Cosmos. Before TASK-240 this call published the
        // transaction onto the process-wide connector and the async write path ignored it anyway.
        store.SetTransactionContext(uow.Context);
        try
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "via-hook", Amount = 1 });
        }
        finally
        {
            store.SetTransactionContext(null);
        }

        await uow.RollbackAsync();
        (await CountAsync(store)).Should().Be(0,
            "a write made under SetTransactionContext must be inside the boundary");
    }

    [Fact]
    public async Task SetTransactionContext_no_longer_publishes_the_transaction_to_the_shared_connector()
    {
        var (store, _) = NewStore("hook-isolation.db");
        var other = new AsyncSQLiteStore<Row>();
        other.SetSettings(new SqLiteSettings(_root, "hook-isolation.db"));
        ReferenceEquals(store.Connector, other.Connector).Should().BeTrue();

        // Run the whole boundary in its own flow, so the ambient scope cannot cover the assertions made
        // after it. (An ambient boundary covering every store in ITS OWN flow is the design — that is
        // what makes a multi-store service method work without per-store wiring. What must not happen is
        // the transaction reaching a DIFFERENT flow, which is what publishing it onto the shared
        // connector did.)
        await Task.Run(async () =>
        {
            await using var uow = NewUnitOfWork(store);
            await uow.BeginAsync();
            store.SetTransactionContext(uow.Context);
            try
            {
                // The connector is shared. If SetTransactionContext still called SetExternalTransaction,
                // this would be non-null and every other caller against this database — on any thread —
                // would silently be enlisted.
                store.Connector!.ExternalTransaction.Should().BeNull(
                    "one caller's transaction must never be published onto a process-wide connector");
                store.Connector!.ExternalConnection.Should().BeNull();

                await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "in-boundary", Amount = 1 });
            }
            finally
            {
                store.SetTransactionContext(null);
            }

            await uow.RollbackAsync();
        }).WaitAsync(TimeSpan.FromSeconds(60));

        // This flow never entered the boundary and shares only the connector.
        AmbientSqlTransaction.Current.Should().BeNull();
        await other.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "other-flow", Amount = 2 });

        var rows = (await other.ReadAsync(CancellationToken.None)).ToList();
        rows.Should().ContainSingle("the rolled-back boundary must leave nothing behind");
        rows[0].Name.Should().Be("other-flow");
    }

    // ---------------------------------------------------------------- capabilities

    [Fact]
    public async Task The_sql_unit_of_work_states_what_it_promises()
    {
        var (store, _) = NewStore();
        await using var uow = NewUnitOfWork(store);

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.Atomic);
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.Database);
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeTrue();
        uow.Capabilities.RequiresServerTopology.Should().BeFalse();
    }
}
