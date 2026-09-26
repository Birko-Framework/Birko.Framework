using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-498 — <c>PropertyUpdate.Increment</c> / <c>Decrement</c> against a real on-disk SQLite database.
/// <para>
/// The fragments are asserted in <c>Birko.Data.SQL.Tests.PropertyUpdateSqlTranslatorTests</c>; this suite asserts
/// what a caller sees: the values that land, that a Set and an Increment travel as <b>one</b> UPDATE (captured from
/// the connector's <c>OnExecute</c>), and that concurrent increments on one row are not lost — the whole reason
/// the operation exists, since a read-modify-write counter already worked single-threaded.
/// </para>
/// </summary>
public class PropertyUpdateIncrementEndToEndTests : IDisposable
{
    private readonly string _root;

    public PropertyUpdateIncrementEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-increment-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("Counters")]
    public class Counter : AbstractModel
    {
        public string Name { get; set; } = null!;
        public int Hits { get; set; }
        public long Total { get; set; }
        public double Ratio { get; set; }
        public float Weight { get; set; }

        /// <summary>No declared precision → <c>REAL</c> on SQLite.</summary>
        public decimal Amount { get; set; }

        /// <summary>Declared precision → <c>NUMERIC(18,2)</c> on SQLite.</summary>
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Price { get; set; }
    }

    private const string DbName = "increment.db";

    private void CreateTable()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = DbName });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Counter) });
    }

    private AsyncSQLiteStore<Counter> NewAsyncStore()
    {
        var store = new AsyncSQLiteStore<Counter>();
        store.SetSettings(new SqLiteSettings(_root, DbName));
        return store;
    }

    private SQLiteStore<Counter> NewSyncStore()
    {
        var store = new SQLiteStore<Counter>();
        store.SetSettings(new SqLiteSettings(_root, DbName));
        return store;
    }

    private async Task<(AsyncSQLiteStore<Counter> Store, Counter Target, Counter Bystander)> SeedAsync()
    {
        CreateTable();
        var store = NewAsyncStore();
        var target = new Counter { Guid = Guid.NewGuid(), Name = "target", Hits = 10, Total = 100, Ratio = 1.5, Amount = 10.10m, Price = 10.10m };
        var bystander = new Counter { Guid = Guid.NewGuid(), Name = "bystander", Hits = 10, Total = 100, Ratio = 1.5, Amount = 10.10m, Price = 10.10m };
        await store.CreateAsync(target);
        await store.CreateAsync(bystander);
        return (store, target, bystander);
    }

    private static async Task<Counter> ReadAsync(AsyncSQLiteStore<Counter> store, Guid guid)
        => (await store.ReadAsync(x => x.Guid == guid)).Single();

    [Fact]
    public async Task SetAndIncrement_RunAsOneUpdateStatement_AndOtherRowsAreUntouched()
    {
        var (store, target, bystander) = await SeedAsync();
        var statements = new ConcurrentQueue<string>();
        store.Connector!.OnExecute += text => statements.Enqueue(text);

        await store.UpdateAsync(
            x => x.Guid == target.Guid!.Value,
            new PropertyUpdate<Counter>().Set(x => x.Name, "renamed").Increment(x => x.Hits, 1));

        var updates = statements.Where(s => s.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)).ToList();
        updates.Should().ContainSingle("a Set and an Increment in one PropertyUpdate are one statement");
        // OnExecute logs the statement with its parameter values rendered inline, hence no '@SET…' here.
        updates[0].Should().Contain("Hits = Hits + ").And.Contain("Name = ");

        var after = await ReadAsync(store, target.Guid!.Value);
        after.Name.Should().Be("renamed");
        after.Hits.Should().Be(11);

        var untouched = await ReadAsync(store, bystander.Guid!.Value);
        untouched.Name.Should().Be("bystander");
        untouched.Hits.Should().Be(10);
    }

    [Fact]
    public async Task Increment_AddsToTheStoredValue_RatherThanAssigningTheDelta()
    {
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target.Guid!.Value, new PropertyUpdate<Counter>().Increment(x => x.Total, 5L));

        (await ReadAsync(store, target.Guid!.Value)).Total.Should().Be(105, "+5 on 100, never '= 5'");
    }

    [Fact]
    public async Task Decrement_Subtracts()
    {
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target.Guid!.Value, new PropertyUpdate<Counter>().Decrement(x => x.Hits, 3));

        (await ReadAsync(store, target.Guid!.Value)).Hits.Should().Be(7);
    }

    [Fact]
    public async Task DoubleIncrement_Adds()
    {
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target.Guid!.Value, new PropertyUpdate<Counter>().Increment(x => x.Ratio, 0.25));

        (await ReadAsync(store, target.Guid!.Value)).Ratio.Should().Be(1.75);
    }

    [Fact]
    public async Task FloatIncrement_Adds()
    {
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target.Guid!.Value, new PropertyUpdate<Counter>().Increment(x => x.Weight, 0.5f));

        (await ReadAsync(store, target.Guid!.Value)).Weight.Should().Be(0.5f);
    }

    /// <summary>
    /// Measured 2026-09-26 (rule 54): <c>10.10m + 0.20m</c> reads back as <c>10.299999999999999m</c> under
    /// <b>both</b> SQLite mappings — <c>REAL</c> and <c>NUMERIC(18,2)</c> alike, because SQLite keeps a non-integer
    /// NUMERIC as an 8-byte float and adds in binary floating point. A Set of <c>10.30m</c> round-trips exactly
    /// (the control below), so an increment <b>does</b> expose this: a decimal counter on SQLite drifts. Documented
    /// in Birko.Data.Stores/README.md; the assertions pin the measured value so a change in it is noticed.
    /// </summary>
    [Fact]
    public async Task DecimalIncrement_OnSqlite_AddsInBinaryFloatingPoint_UnderBothMappings()
    {
        var (store, target, bystander) = await SeedAsync();

        await store.UpdateAsync(
            x => x.Guid == target.Guid!.Value,
            new PropertyUpdate<Counter>().Increment(x => x.Amount, 0.20m).Increment(x => x.Price, 0.20m));
        await store.UpdateAsync(
            x => x.Guid == bystander.Guid!.Value,
            new PropertyUpdate<Counter>().Set(x => x.Amount, 10.30m).Set(x => x.Price, 10.30m));

        var after = await ReadAsync(store, target.Guid!.Value);
        after.Amount.Should().Be(10.299999999999999m, "REAL column: binary floating-point addition, measured");
        after.Price.Should().Be(10.299999999999999m, "NUMERIC(18,2) column: stored as a float too, measured");

        var control = await ReadAsync(store, bystander.Guid!.Value);
        control.Amount.Should().Be(10.30m, "a Set round-trips exactly");
        control.Price.Should().Be(10.30m, "a Set round-trips exactly");
    }

    [Fact]
    public void Sync_SetAndIncrement_Apply()
    {
        CreateTable();
        var store = NewSyncStore();
        var target = new Counter { Guid = Guid.NewGuid(), Name = "target", Hits = 10 };
        store.Create(target);

        store.Update(
            x => x.Guid == target.Guid!.Value,
            new PropertyUpdate<Counter>().Set(x => x.Name, "renamed").Increment(x => x.Hits, 2).Decrement(x => x.Total, 1L));

        var after = store.ReadFirst(x => x.Guid == target.Guid!.Value)!;
        after.Name.Should().Be("renamed");
        after.Hits.Should().Be(12);
        after.Total.Should().Be(-1);
    }

    /// <summary>
    /// The property the operation exists for. Every caller uses its own store instance, as separate requests
    /// would. Proven able to fail: with the translator emitting <c>col = @p</c> for an increment the row ends at 1.
    /// </summary>
    [Fact]
    public async Task ParallelIncrements_OnOneRow_AreNotLost()
    {
        const int callers = 50;
        var (_, target, _) = await SeedAsync();
        var guid = target.Guid!.Value;

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
            NewAsyncStore().UpdateAsync(x => x.Guid == guid, new PropertyUpdate<Counter>().Increment(x => x.Hits, 1)))));

        (await ReadAsync(NewAsyncStore(), guid)).Hits.Should().Be(10 + callers);
    }
}
