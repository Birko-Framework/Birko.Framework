using System;
using System.IO;
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
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-244 — what a store believes after its schema-ensure was rolled back, and what the next operation
/// does with that belief.
/// </summary>
/// <remarks>
/// <para>
/// This is the framework-level reproduction of the symptom Symbio reported (its TASK-527): on a wiped
/// SQLite database an operation returned success while one table was never created, every table the same
/// operation wrote afterwards existed and was populated, and the failure was permanent for the process.
/// The consumer could not reproduce it on demand — four from-scratch bring-ups all succeeded — so it is
/// reproduced here from the ordering instead.
/// </para>
/// <para>
/// <b>Two framework behaviours compose into it, and neither is visible alone.</b>
/// </para>
/// <list type="number">
/// <item><b>The residue this task owns.</b> A store's lazy schema-ensure runs inside the caller's boundary
/// (SQLite's DDL is transactional, and TASK-243 measured that it must stay on the boundary's connection
/// there or deadlock). If that boundary rolls back, the table goes with it — but
/// <c>AbstractAsyncStore._initialized</c> was already set to true, so the same store instance never
/// schema-ensures again.</item>
/// <item><b>The swallow that turns it into silent data loss.</b> <c>SqLiteConnector</c>'s
/// <c>OnException</c> handler answers "no such table" by calling <c>DoInit()</c> and <b>not
/// rethrowing</b> — and <c>DoInit</c> only raises the <c>OnInit</c> event, which nothing in the framework
/// subscribes to. So a write against the missing table reports success, stores nothing, and does not
/// create the table.</item>
/// </list>
/// </remarks>
public class SchemaEnsureRollbackResidueTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;
    private static int _seq;

    public SchemaEnsureRollbackResidueTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-residue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Seed : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class SeedMapping : IModelMapping<Seed>
    {
        public void Configure(ModelMap<Seed> map)
        {
            map.ToTable("ResidueSeeds").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private SqLiteSettings NewDatabase()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new SeedMapping());
        registry.ApplyToDatabase();

        return new SqLiteSettings(_root, $"residue{Interlocked.Increment(ref _seq)}.db")
        {
            CommandTimeout = 3,
        };
    }

    private static AsyncSQLiteStore<Seed> AsyncStore(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<Seed>();
        store.SetSettings(settings);
        return store;
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
    /// The whole chain, deterministically: a rolled-back boundary, then a perfectly ordinary write on the
    /// same store instance that reports success and stores nothing.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_schema_ensure_leaves_the_store_believing_it_is_initialised()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        // 1. First data access happens inside a boundary that rolls back — an earlier failed attempt, a
        //    validation that bailed, an exception out of the caller's RunAsync. The CREATE TABLE was part
        //    of the boundary, so it is gone.
        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new Seed { Guid = Guid.NewGuid(), Name = "first attempt" });
            await uow.RollbackAsync();
        }

        TableExists(settings, "ResidueSeeds").Should().BeFalse("the DDL rolled back with the boundary");

        // 2. The next operation on the SAME store instance. No boundary at all this time — the plainest
        //    write there is.
        var guid = await store.CreateAsync(new Seed { Guid = Guid.NewGuid(), Name = "second attempt" });

        _output.WriteLine($"CreateAsync returned {guid}; table exists = {TableExists(settings, "ResidueSeeds")}");

        // 3. What SHOULD happen: schema-ensure re-runs (or the write fails loudly). What DOES happen is the
        //    subject of this test.
        TableExists(settings, "ResidueSeeds").Should().BeTrue(
            "the store must not skip schema-ensure after a schema-ensure that was rolled back — otherwise "
          + "the write below lands nowhere");

        var read = await store.ReadAsync(x => x.Name == "second attempt");
        read.Should().NotBeNull("the write reported success, so the row must be readable");
    }

    /// <summary>
    /// <b>The two doors, measured against each other.</b> The acceptance for this task asks for one answer
    /// applied identically to the ambient door (<c>SqlUnitOfWork</c>) and the <c>SetTransactionContext</c>
    /// door. They disagree today, and this is the measurement of it: <c>EnterTransactionScope()</c> is
    /// called inside <c>*Core</c>, i.e. AFTER <c>EnsureInitializedAsync</c> has already run, so with the
    /// per-store door the ambient is not published while schema-ensure runs and the DDL goes onto a
    /// connection of its own — committing immediately, outside the caller's transaction.
    /// </summary>
    [Fact]
    public async Task The_per_store_transaction_door_puts_schema_ensure_outside_the_caller_transaction()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        using var connection = new SqliteConnection(settings.GetConnectionString());
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        store.SetTransactionContext(new SqlTransactionContext(connection, transaction));
        await store.CreateAsync(new Seed { Guid = Guid.NewGuid(), Name = "per-store door" });
        transaction.Rollback();
        store.SetTransactionContext(null);

        // The row is gone with the rollback either way; the question is the TABLE.
        TableExists(settings, "ResidueSeeds").Should().BeFalse(
            "the ambient door puts the CREATE TABLE inside the boundary, so a rollback removes it — if this "
          + "table survives, the per-store door ran the DDL on its own connection and the two doors disagree "
          + "about what 'inside a transaction' means");
    }

    /// <summary>
    /// ⚠ <b>Pins a DEFECT, not a contract — owned by TASK-277.</b> A write against a table that does not
    /// exist reports SUCCESS on SQLite: <c>SqLiteConnector.OnException</c> answers "no such table" by
    /// calling <c>DoInit()</c> and <b>not rethrowing</b>, and <c>DoInit</c> only raises the <c>OnInit</c>
    /// event, which nothing in the framework subscribes to. So the statement is discarded, the table is not
    /// created, and the caller is told it worked.
    /// </summary>
    /// <remarks>
    /// This test asserts the measured behaviour so that the defect is recorded rather than believed, and so
    /// TASK-277 has something to invert. It is deliberately NOT fixed here: TASK-244 owns the ordering, and
    /// this is the swallow family (TASK-211 narrowed the same decision on PostgreSQL and MySQL and did not
    /// touch SQLite). It matters because it is what turned this task's residue into Symbio's report — the
    /// residue alone loses one operation, the swallow makes that operation answer 200.
    /// </remarks>
    [Fact]
    public async Task A_write_to_a_missing_table_reports_success_on_sqlite_TASK277()
    {
        var settings = NewDatabase();
        var store = AsyncStore(settings);

        // Force the store to believe it is initialised without the table existing — exactly the state a
        // rolled-back schema-ensure leaves behind, reached here without a boundary so the two halves are
        // measured separately.
        await store.InitAsync();
        using (var connection = new SqliteConnection(settings.GetConnectionString()))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE \"ResidueSeeds\"";
            drop.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var guid = await store.CreateAsync(new Seed { Guid = Guid.NewGuid(), Name = "lost" });

        guid.Should().NotBe(Guid.Empty, "the call reports success — this is the defect, not the contract");
        TableExists(settings, "ResidueSeeds").Should().BeFalse(
            "and DoInit() does not create the table either: it raises OnInit, which nothing in the framework "
          + "subscribes to");

        // The row is simply gone. Read through a connection of our own rather than through the store, whose
        // read path would meet the same swallow and answer 'empty' either way.
        using var check = new SqliteConnection(settings.GetConnectionString());
        check.Open();
        using var exists = check.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ResidueSeeds'";
        Convert.ToInt32(exists.ExecuteScalar()).Should().Be(0,
            "TASK-277: a write that cannot be applied must fail loudly instead of being discarded");
    }
}
