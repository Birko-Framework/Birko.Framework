using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// TASK-243, the half that says why the fix is a <b>provider switch</b> and not a blanket rule.
///
/// <para>
/// A store initialises lazily, so its first data access issues <c>CREATE TABLE IF NOT EXISTS</c> from
/// inside the public CRUD wrapper — inside the caller's boundary, if one is open. On MySQL that DDL
/// silently committed the boundary, so schema DDL is now issued <i>off</i> it there
/// (<see cref="AbstractConnectorBase.SupportsTransactionalDdl"/> is false for MySQL alone).
/// </para>
///
/// <para>
/// <b>Doing that unconditionally would have replaced a silent bug with a hang.</b> SQLite serialises at
/// the file level: a boundary that has written holds the RESERVED lock, and a second connection asking to
/// write blocks for the whole busy timeout and then fails. So the provider whose DDL <i>must</i> stay on
/// the boundary's connection is exactly the provider whose DDL is transactional, and the one that needs
/// it off is exactly the one where a second connection is legal. The two halves of the trade land on
/// opposite providers, which is what makes the switch safe rather than lucky.
/// </para>
///
/// <para>
/// These tests pin that: a store initialising inside a boundary that <b>already holds the write lock</b>
/// completes promptly and correctly, and rolls back with the boundary. They fail as a timeout, not as an
/// assertion, if someone makes the suppression unconditional — which is the failure mode worth catching,
/// because a hang in a consumer reads as an outage rather than as a bug.
/// </para>
/// </summary>
public class LazyInitInsideBoundaryEndToEndTests : IDisposable
{
    private readonly string _root;
    private static int _seq;

    public LazyInitInsideBoundaryEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-lazyinit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class RowA : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    public class RowB : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class RowAMapping : IModelMapping<RowA>
    {
        public void Configure(ModelMap<RowA> map)
        {
            map.ToTable("LazyRowsA").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private sealed class RowBMapping : IModelMapping<RowB>
    {
        public void Configure(ModelMap<RowB> map)
        {
            map.ToTable("LazyRowsB").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    /// <remarks>
    /// <c>CommandTimeout = 3</c>: if the DDL ever moves off the boundary's connection on SQLite these
    /// tests must fail in seconds rather than sit on the default 30s busy timeout per statement.
    /// </remarks>
    private SqLiteSettings NewDatabase()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowAMapping());
        registry.Register(new RowBMapping());
        registry.ApplyToDatabase();

        var settings = new SqLiteSettings(_root, $"lazyinit{Interlocked.Increment(ref _seq)}.db")
        {
            CommandTimeout = 3,
        };
        // Only A exists up front. B's table is created by B's own lazy schema-ensure, inside the boundary.
        var connector = new SqLiteConnector(settings);
        connector.CreateTable(new[] { typeof(RowA) });
        return settings;
    }

    private static AsyncSQLiteStore<T> AsyncStore<T>(SqLiteSettings settings) where T : AbstractModel
    {
        var store = new AsyncSQLiteStore<T>();
        store.SetSettings(settings);
        return store;
    }

    private static int CommittedCount(SqLiteSettings settings, string table)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool TableExists(SqLiteSettings settings, string table)
    {
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @t";
        command.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// The decisive one: the boundary has already written, so it holds SQLite's write lock, and only then
    /// does a second store run its lazy <c>CREATE TABLE</c>.
    /// </summary>
    [Fact]
    public async Task Schema_ensure_inside_a_boundary_that_holds_the_write_lock_does_not_block()
    {
        var settings = NewDatabase();
        var writer = AsyncStore<RowA>(settings);
        var cold = AsyncStore<RowB>(settings);

        TableExists(settings, "LazyRowsB").Should().BeFalse("B's table must be created inside the boundary");

        var stopwatch = Stopwatch.StartNew();
        await using (var uow = SqlUnitOfWork.FromStore(writer))
        {
            await uow.BeginAsync();
            // Takes the RESERVED lock. A second connection cannot write past this point.
            await writer.CreateAsync(new RowA { Guid = Guid.NewGuid(), Name = "locking", Amount = 1 });

            // Cold store: EnsureInitializedAsync -> CREATE TABLE. Must join the boundary's connection.
            await cold.CreateAsync(new RowB { Guid = Guid.NewGuid(), Name = "inside", Amount = 2 });

            await uow.RollbackAsync();
        }
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "the DDL must run on the boundary's own connection — routing it off would block on the "
          + "RESERVED lock the boundary holds and fail on the busy timeout");

        TableExists(settings, "LazyRowsB").Should().BeFalse(
            "SQLite DDL is transactional, so the table created inside the boundary went with the rollback");
        CommittedCount(settings, "LazyRowsA").Should().Be(0);
    }

    [Fact]
    public async Task A_store_initialising_inside_a_boundary_still_rolls_back_its_writes()
    {
        var settings = NewDatabase();
        var store = AsyncStore<RowA>(settings);   // deliberately NOT warmed up

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new List<RowA>
            {
                new RowA { Guid = Guid.NewGuid(), Name = "a", Amount = 1 },
                new RowA { Guid = Guid.NewGuid(), Name = "b", Amount = 2 },
            }, null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount(settings, "LazyRowsA").Should().Be(0);
    }

    [Fact]
    public async Task A_committed_boundary_around_a_stores_first_operation_still_persists()
    {
        var settings = NewDatabase();
        var store = AsyncStore<RowB>(settings);

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new RowB { Guid = Guid.NewGuid(), Name = "kept", Amount = 1 });
            await uow.CommitAsync();
        }

        TableExists(settings, "LazyRowsB").Should().BeTrue(
            "the CREATE TABLE was part of the boundary and the boundary committed");
        CommittedCount(settings, "LazyRowsB").Should().Be(1);
    }
}
