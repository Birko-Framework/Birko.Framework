using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// The <b>bulk</b> half of the transaction boundary, on a real on-disk SQLite database.
///
/// <para>
/// TASK-240 taught the single-command paths to join an open <see cref="AmbientSqlTransaction"/> boundary
/// and left every bulk path behind: <c>BulkInsert</c> / <c>BulkUpdate</c> / <c>BulkDelete</c> and their
/// async twins opened their <i>own</i> connection and their own transaction unconditionally. Every
/// collection-shaped repository write routes through them, so create-many, update-many, delete-many,
/// delete-where and delete-all all escaped the caller's boundary. Measured in a consumer (Symbio
/// TASK-442): 20 of 158 boundary-wrapped service operations broke on it.
/// </para>
///
/// <para>
/// <b>These tests count rows.</b> On SQLite the escape is loud — the second connection cannot take the
/// write lock the boundary holds, so it blocks for the command timeout and fails — but on PostgreSQL,
/// MySQL and MSSql two connections are perfectly legal and the escaped write simply <i>commits</i> and
/// survives the owner's rollback with no error anywhere. A suite that asserted "no exception was thrown"
/// would pass against the broken code on all four. Rows are the only honest evidence, so the counts here
/// are deliberately taken after the boundary has been rolled back.
/// </para>
///
/// <para>
/// The async half runs through <see cref="SqlUnitOfWork"/>. The sync half has no unit of work — its door
/// is <c>SetTransactionContext</c> + <c>DataBaseStore.EnterTransactionScope</c> — and it matters just as
/// much: sync single-row writes already honoured a boundary while sync bulk writes did not, on the very
/// same store.
/// </para>
/// </summary>
public class BulkTransactionBoundaryEndToEndTests : IDisposable
{
    private readonly string _root;
    private static int _seq;

    public BulkTransactionBoundaryEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-bulktx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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
            map.ToTable("BulkTxRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    /// <summary>
    /// A fresh database file per test, so the process-wide connector cache in <c>DataBase.GetConnector</c>
    /// hands out a distinct connector and nothing leaks between tests.
    /// </summary>
    /// <remarks>
    /// <c>CommandTimeout = 2</c> deliberately: against the <i>unfixed</i> connector a bulk write issued
    /// inside an open boundary waits out SQLite's busy timeout before failing, and the default 30s turns a
    /// revert check into a multi-minute one. It changes nothing about the fixed behaviour.
    /// </remarks>
    private SqLiteSettings NewDatabase()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();

        var settings = new SqLiteSettings(_root, $"bulktx{Interlocked.Increment(ref _seq)}.db")
        {
            CommandTimeout = 2,
        };
        var connector = new SqLiteConnector(settings);
        connector.CreateTable(new[] { typeof(Row) });
        return settings;
    }

    private static AsyncSQLiteStore<Row> AsyncStore(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(settings);
        return store;
    }

    private static SQLiteStore<Row> SyncStore(SqLiteSettings settings)
    {
        var store = new SQLiteStore<Row>();
        store.SetSettings(settings);
        return store;
    }

    private static List<Row> Rows(params string[] names)
        => names.Select((n, i) => new Row { Guid = Guid.NewGuid(), Name = n, Amount = i + 1 }).ToList();

    /// <summary>
    /// Counts through a connection of its own, so the answer is what is <b>committed</b> rather than what
    /// some still-open transaction can see.
    /// </summary>
    private static int CommittedCount(SqLiteSettings settings)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"BulkTxRows\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // ================================================================ async bulk

    [Fact]
    public async Task Async_bulk_create_inside_a_rolled_back_boundary_leaves_nothing()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount(settings).Should().Be(0,
            "a bulk insert issued inside a boundary must be undone by that boundary's rollback");
    }

    [Fact]
    public async Task Async_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        await store.CreateAsync(Rows("a", "b"), null, CancellationToken.None);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();
        foreach (var row in loaded) row.Amount = 999;

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.UpdateAsync(loaded, null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        var after = (await store.ReadAsync(CancellationToken.None)).ToList();
        after.Should().HaveCount(2);
        after.Should().OnlyContain(r => r.Amount != 999,
            "a bulk update issued inside a boundary must be undone by that boundary's rollback");
    }

    [Fact]
    public async Task Async_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);
        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.DeleteAsync(loaded, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount(settings).Should().Be(3,
            "a bulk delete issued inside a boundary must be undone by that boundary's rollback");
    }

    [Fact]
    public async Task Async_bulk_writes_in_a_committed_boundary_all_persist()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
            await uow.CommitAsync();
        }

        CommittedCount(settings).Should().Be(3,
            "joining a boundary must not cost the rows their durability — the owner's commit is what "
          + "makes them durable");
    }

    [Fact]
    public async Task Async_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        await store.CreateAsync(Rows("a", "b", "c"), null, CancellationToken.None);
        CommittedCount(settings).Should().Be(3);

        var loaded = (await store.ReadAsync(CancellationToken.None)).ToList();
        foreach (var row in loaded) row.Amount = 42;
        await store.UpdateAsync(loaded, null, CancellationToken.None);
        (await store.ReadAsync(CancellationToken.None)).Should().OnlyContain(r => r.Amount == 42);

        await store.DeleteAsync(loaded, CancellationToken.None);
        CommittedCount(settings).Should().Be(0);
    }

    // ================================================================ sync bulk

    /// <summary>
    /// Runs <paramref name="work"/> against a boundary the caller owns, then rolls it back.
    /// </summary>
    /// <remarks>
    /// The store is warmed up first: <c>EnsureInitialized</c> runs in the public wrapper, <i>before</i> the
    /// Core override publishes the boundary, so a store whose first ever operation happens inside a
    /// boundary would still run its schema-ensure on a connection of its own. That is pre-existing and
    /// orthogonal — but on SQLite it would deadlock the test against its own transaction, and mistaking
    /// that for the defect under test is exactly the trap.
    /// </remarks>
    private static void InRolledBackBoundary(SQLiteStore<Row> store, SqLiteSettings settings, Action work)
    {
        _ = store.Read().ToList();

        using var connection = new SqliteConnection(settings.GetConnectionString());
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
        var settings = NewDatabase();
        var store = SyncStore(settings);

        InRolledBackBoundary(store, settings, () => store.Create(Rows("a", "b", "c")));

        CommittedCount(settings).Should().Be(0,
            "against the unfixed sync connector the insert ran on a second connection and survived the "
          + "rollback; the sync store publishes the same ambient boundary the async one does");
    }

    [Fact]
    public void Sync_bulk_update_inside_a_rolled_back_boundary_is_discarded()
    {
        var settings = NewDatabase();
        var store = SyncStore(settings);
        store.Create(Rows("a", "b"));

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 999;

        InRolledBackBoundary(store, settings, () => store.Update(loaded));

        store.Read().Should().HaveCount(2).And.OnlyContain(r => r.Amount != 999);
    }

    [Fact]
    public void Sync_bulk_delete_inside_a_rolled_back_boundary_leaves_the_rows()
    {
        var settings = NewDatabase();
        var store = SyncStore(settings);
        store.Create(Rows("a", "b", "c"));

        var loaded = store.Read().ToList();

        InRolledBackBoundary(store, settings, () => store.Delete(loaded));

        CommittedCount(settings).Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_in_a_committed_boundary_all_persist()
    {
        var settings = NewDatabase();
        var store = SyncStore(settings);
        _ = store.Read().ToList();

        using (var connection = new SqliteConnection(settings.GetConnectionString()))
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

        CommittedCount(settings).Should().Be(3);
    }

    [Fact]
    public void Sync_bulk_writes_without_a_boundary_commit_immediately_exactly_as_before()
    {
        var settings = NewDatabase();
        var store = SyncStore(settings);

        store.Create(Rows("a", "b", "c"));
        CommittedCount(settings).Should().Be(3);

        var loaded = store.Read().ToList();
        foreach (var row in loaded) row.Amount = 42;
        store.Update(loaded);
        store.Read().Should().OnlyContain(r => r.Amount == 42);

        store.Delete(loaded);
        CommittedCount(settings).Should().Be(0);
    }
}
