using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class T877Row : AbstractModel
{
    public string? Code { get; set; }
    public string? Grp { get; set; }
    public int A { get; set; }
    public int? Score { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// TASK-877, live RavenDB: <c>Aggregate</c> / <c>AggregateAsync</c> with one GROUP BY field and no time bucket. They sent a
/// facet query against the collection, which RavenDB refuses for every filter ("Facet query must be executed against static
/// index"). By the user's decision (2026-11-09) they take the client-side LINQ path the multi-field GROUP BY already took.
/// The oracle is <see cref="AggregateHelper.LinqAggregate{T}"/> over the same rows held in memory, the reference the
/// InMemory store answers with. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task877SingleFieldAggregateLiveTests : IDisposable
{
    private readonly RavenLive _live;
    private readonly ITestOutputHelper _output;

    public Task877SingleFieldAggregateLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new RavenLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static readonly int? Nul = null;
    private static readonly string[] Codes = { "r2", "r5", "r7" };

    private static readonly IReadOnlyDictionary<string, Expression<Func<T877Row, bool>>?> Filters =
        new Dictionary<string, Expression<Func<T877Row, bool>>?>
        {
            ["no filter"] = null,
            ["A > 2"] = x => x.A > 2,
            ["Grp == g1"] = x => x.Grp == "g1",
            ["Score > nul"] = x => x.Score > Nul,
            ["!(Score > nul) && A > 4"] = x => !(x.Score > Nul) && x.A > 4,
            ["!(Score > nul || A > 3)"] = x => !(x.Score > Nul || x.A > 3),
            ["Score != nul"] = x => x.Score != Nul,
            ["codes.Contains(Code)"] = x => Codes.Contains(x.Code),
            ["!codes.Contains(Code)"] = x => !Codes.Contains(x.Code),
            ["x => true"] = x => true,
            ["Score < nul (folds to false)"] = x => x.Score < Nul,
        };

    private static readonly IReadOnlyDictionary<string, AggregateField[]> AggregateSets =
        new Dictionary<string, AggregateField[]>
        {
            ["Count"] = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
            ["Sum/Avg/Min/Max of int"] = new[]
            {
                new AggregateField(AggregateFunction.Sum, "A", "sum"),
                new AggregateField(AggregateFunction.Avg, "A", "avg"),
                new AggregateField(AggregateFunction.Min, "A", "min"),
                new AggregateField(AggregateFunction.Max, "A", "max"),
            },
            ["Sum/Avg/Min/Max of int?"] = new[]
            {
                new AggregateField(AggregateFunction.Sum, "Score", "sum"),
                new AggregateField(AggregateFunction.Avg, "Score", "avg"),
                new AggregateField(AggregateFunction.Min, "Score", "min"),
                new AggregateField(AggregateFunction.Max, "Score", "max"),
            },
            ["Count + Sum/Avg of decimal"] = new[]
            {
                new AggregateField(AggregateFunction.Count, "Amount", "n"),
                new AggregateField(AggregateFunction.Sum, "Amount", "sum"),
                new AggregateField(AggregateFunction.Avg, "Amount", "avg"),
            },
        };

    // Eight rows over three groups, one of them the null group, which a facet named "NULL_VALUE" and LINQ keys as null.
    private static List<T877Row> Rows() => new()
    {
        new() { Code = "r1", Grp = "g1", A = 1, Score = null, Amount = 1.10m },
        new() { Code = "r2", Grp = "g2", A = 2, Score = 5, Amount = 2.25m },
        new() { Code = "r3", Grp = "g1", A = 3, Score = null, Amount = 3.00m },
        new() { Code = "r4", Grp = "g2", A = 4, Score = 8, Amount = 0.05m },
        new() { Code = "r5", Grp = "g1", A = 5, Score = null, Amount = 10m },
        new() { Code = "r6", Grp = "g2", A = 6, Score = 2, Amount = 7.77m },
        new() { Code = "r7", Grp = null, A = 7, Score = 3, Amount = 1m },
        new() { Code = "r8", Grp = null, A = 8, Score = null, Amount = 2m },
    };

    private static AggregateQuery<T877Row> Query(Expression<Func<T877Row, bool>>? filter, AggregateField[] aggregates) => new()
    {
        Filter = filter,
        GroupByFields = new[] { "Grp" },
        Aggregates = aggregates,
    };

    /// <summary>
    /// A result set as sorted "key|col=value|…" lines, values formatted invariantly with their CLR type name. A decimal is
    /// written without trailing zeros: a stored <c>1m</c> reads back from RavenDB as <c>1.0m</c> (equal, another scale;
    /// measured), so its sum differs from the in-memory oracle's only in scale.
    /// </summary>
    private static string Render(IEnumerable<AggregateResult> results)
        => string.Join(" ; ", results
            .Select(r => string.Join("|", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + Format(kv.Value))))
            .OrderBy(x => x, StringComparer.Ordinal));

    private static string Format(object? value) => value switch
    {
        null => "null",
        decimal d => "Decimal:" + d.ToString("G29", CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    [Fact]
    public async Task Single_field_group_by_answers_what_LINQ_answers_over_the_same_rows_sync_and_async()
    {
        if (!_live.Ready()) return;
        var asyncStore = _live.Track((url, db) => new AsyncRavenDBStore<T877Row>(url, db));
        var syncStore = _live.Track((url, db) => new RavenDBStore<T877Row>(url, db));
        foreach (var row in Rows()) await asyncStore.CreateAsync(row);
        foreach (var row in Rows()) syncStore.Create(row);

        var failures = new List<string>();
        var cases = 0;
        foreach (var (filterName, filter) in Filters)
        foreach (var (aggName, aggregates) in AggregateSets)
        {
            var expected = Render(AggregateHelper.LinqAggregate(Rows(), Query(filter, aggregates)));
            var label = $"{aggName} where {filterName}";
            cases++;
            try
            {
                var got = Render(await asyncStore.AggregateAsync(Query(filter, aggregates)));
                if (got != expected) failures.Add($"AggregateAsync {label}: LINQ [{expected}], got [{got}]");
            }
            catch (Exception ex)
            {
                failures.Add($"AggregateAsync {label}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            try
            {
                var got = Render(syncStore.Aggregate(Query(filter, aggregates)));
                if (got != expected) failures.Add($"Aggregate {label}: LINQ [{expected}], got [{got}]");
            }
            catch (Exception ex)
            {
                failures.Add($"Aggregate {label}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        _output.WriteLine($"{cases} cases x sync/async, {failures.Count} failures");
        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_field_group_by_orders_and_pages_the_groups_as_LINQ_does()
    {
        if (!_live.Ready()) return;
        var asyncStore = _live.Track((url, db) => new AsyncRavenDBStore<T877Row>(url, db));
        var syncStore = _live.Track((url, db) => new RavenDBStore<T877Row>(url, db));
        foreach (var row in Rows()) await asyncStore.CreateAsync(row);
        foreach (var row in Rows()) syncStore.Create(row);

        AggregateQuery<T877Row> Paged() => new()
        {
            Filter = x => x.A > 1,
            GroupByFields = new[] { "Grp" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "A", "sum") },
            OrderBy = new Dictionary<string, bool> { ["sum"] = true },
            Offset = 1,
            Limit = 1,
        };

        var expected = AggregateHelper.LinqAggregate(Rows(), Paged()).Select(r => r.Values["sum"]).ToList();
        expected.Should().Equal(new object?[] { 12 }, "null = 7+8 = 15, g2 = 2+4+6 = 12, g1 = 3+5 = 8; descending, offset 1");

        (await asyncStore.AggregateAsync(Paged())).Select(r => r.Values["sum"]).Should().Equal(expected);
        syncStore.Aggregate(Paged()).Select(r => r.Values["sum"]).Should().Equal(expected);
    }

    /// <summary>
    /// The LINQ path materialises every matching document in one query (no Take, no streaming) and aggregates in memory.
    /// Measured here: past the old 1024-result page limits of earlier RavenDB versions nothing is cut short.
    /// </summary>
    [Fact]
    public async Task Single_field_group_by_counts_every_matching_document_past_a_page()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<T877Row>(url, db));
        const int total = 2600;
        var rows = Enumerable.Range(0, total)
            .Select(i => new T877Row { Code = "r" + i, Grp = "g" + (i % 3), A = i, Amount = i % 7 })
            .ToList();
        await store.CreateAsync(rows);

        var query = new AggregateQuery<T877Row>
        {
            Filter = x => x.A >= 0,
            GroupByFields = new[] { "Grp" },
            Aggregates = new[]
            {
                new AggregateField(AggregateFunction.Count, "A", "n"),
                new AggregateField(AggregateFunction.Sum, "A", "sum"),
            },
        };

        var got = await store.AggregateAsync(query);
        Render(got).Should().Be(Render(AggregateHelper.LinqAggregate(rows, query)));
        got.Sum(r => Convert.ToInt32(r.Values["n"], CultureInfo.InvariantCulture)).Should().Be(total);
    }
}
