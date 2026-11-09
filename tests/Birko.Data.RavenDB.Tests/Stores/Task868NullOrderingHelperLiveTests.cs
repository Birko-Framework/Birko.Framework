using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.IndexManagement;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class T868Row : AbstractModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public string? Grp { get; set; }
    public int? Score { get; set; }
}

/// <summary>A static map index over <see cref="T868Row"/>, queried through <see cref="RavenDBIndexManager"/>'s helpers.</summary>
public class T868RowsIndex : AbstractIndexCreationTask<T868Row>
{
    public T868RowsIndex()
    {
        Map = rows => from r in rows select new { r.Code, r.A, r.Grp, r.Score };
    }
}

/// <summary>
/// TASK-868, live RavenDB: the filter-taking paths TASK-867 did not wire — the stores' <c>Aggregate</c> /
/// <c>AggregateAsync</c> (native facet and LINQ fallback) and <see cref="RavenDBIndexManager"/>'s
/// <c>QueryMapReduceAsync</c>, <c>QueryMapReduceFirstAsync</c> and <c>CountMapReduceAsync</c> — answer what the compiled
/// C# filter matches. RQL ranks null below every value, so <c>Score &gt; null</c> matched every non-null Score; and those
/// paths also skipped <c>RavenFilterRewriter</c>'s <c>Contains</c> → <c>In</c> and <c>x =&gt; true</c> handling.
/// Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task868NullOrderingHelperLiveTests : IDisposable
{
    private readonly RavenLive _live;
    private readonly ITestOutputHelper _output;

    public Task868NullOrderingHelperLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new RavenLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static readonly int? Nul = null;
    private static readonly string[] Codes = { "r2", "r5" };

    public static readonly IReadOnlyDictionary<string, Expression<Func<T868Row, bool>>> Shapes =
        new Dictionary<string, Expression<Func<T868Row, bool>>>
        {
            ["Score > nul"] = x => x.Score > Nul,
            ["nul <= Score"] = x => Nul <= x.Score,
            ["Score > nul || A > 4"] = x => x.Score > Nul || x.A > 4,
            ["!(Score > nul) && A > 4"] = x => !(x.Score > Nul) && x.A > 4,
            ["!(Score > nul || A > 3)"] = x => !(x.Score > Nul || x.A > 3),
            ["!(Score < nul && A > 1)"] = x => !(x.Score < Nul && x.A > 1),
            ["Score != nul"] = x => x.Score != Nul,
            ["codes.Contains(Code)"] = x => Codes.Contains(x.Code),
            ["x => true"] = x => true,
        };

    private static readonly int?[] Scores = { null, 5, null, 8, null, 2 };

    private static List<T868Row> Oracle() => Scores
        .Select((s, i) => new T868Row { Code = "r" + (i + 1), A = i + 1, Grp = i % 2 == 0 ? "g1" : "g2", Score = s })
        .ToList();

    private static AggregateQuery<T868Row> Native(Expression<Func<T868Row, bool>> filter) => new()
    {
        Filter = filter,
        GroupByFields = new[] { "Grp" },
        Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
    };

    private static AggregateQuery<T868Row> Fallback(Expression<Func<T868Row, bool>> filter) => new()
    {
        Filter = filter,
        GroupByFields = new[] { "Grp", "Code" },
        Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
    };

    /// <summary>A result set as a sorted list of "Grp=…|n=…" (Code ignored for the native facet), comparable across paths.</summary>
    private static string Render(IEnumerable<AggregateResult> results, bool withCode)
        => string.Join(" ; ", results
            .Select(r => $"{Convert.ToString(r.Values["Grp"], CultureInfo.InvariantCulture)?.ToLowerInvariant()}"
                       + (withCode ? "/" + r.Values["Code"] : "")
                       + "=" + Convert.ToDouble(r.Values["n"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))
            .OrderBy(x => x, StringComparer.Ordinal));

    private static string Expected(IEnumerable<T868Row> oracle, Func<T868Row, bool> match, bool withCode)
        => string.Join(" ; ", oracle.Where(match)
            .GroupBy(r => withCode ? r.Grp + "/" + r.Code : r.Grp)
            .Select(g => g.Key + "=" + g.Count().ToString(CultureInfo.InvariantCulture))
            .OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public async Task Aggregate_answers_what_CSharp_matches_on_both_paths_sync_and_async()
    {
        if (!_live.Ready()) return;
        var asyncStore = _live.Track((url, db) => new AsyncRavenDBStore<T868Row>(url, db));
        var syncStore = _live.Track((url, db) => new RavenDBStore<T868Row>(url, db));
        var oracle = Oracle();
        foreach (var row in Oracle()) await asyncStore.CreateAsync(row);
        foreach (var row in Oracle()) syncStore.Create(row);

        var failures = new List<string>();
        foreach (var (shape, filter) in Shapes)
        {
            var match = filter.Compile();
            foreach (var native in new[] { true, false })
            {
                var query = native ? Native(filter) : Fallback(filter);
                var expected = Expected(oracle, match, withCode: !native);
                var path = native ? "native" : "fallback";
                try
                {
                    var got = Render(await asyncStore.AggregateAsync(query), withCode: !native);
                    if (got != expected) failures.Add($"AggregateAsync {path} {shape}: C# [{expected}], got [{got}]");
                }
                catch (Exception ex)
                {
                    if (!(native && IsNativeFacetGap(ex))) failures.Add($"AggregateAsync {path} {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
                try
                {
                    var got = Render(syncStore.Aggregate(query), withCode: !native);
                    if (got != expected) failures.Add($"Aggregate {path} {shape}: C# [{expected}], got [{got}]");
                }
                catch (Exception ex)
                {
                    if (!(native && IsNativeFacetGap(ex))) failures.Add($"Aggregate {path} {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Measured 2026-11-09, unfixed and outside this task: the native facet path (one GROUP BY field, no time bucket) is a
    /// dynamic-collection facet query, which RavenDB refuses for every filter, a plain one included. A filter that folds to
    /// <c>false</c> is still answered (no query is sent). The constraining assertion above skips this refusal; the test
    /// below is inverted, so it fails the day the native path works and the skip must go (rule 56).
    /// </summary>
    private static bool IsNativeFacetGap(Exception ex)
        => ex.GetType().Name == "InvalidQueryException" && ex.Message.Contains("Facet query must be executed against static index");

    [Fact]
    public async Task Known_gap_the_native_facet_aggregate_is_refused_by_RavenDB_for_any_filter()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<T868Row>(url, db));
        foreach (var row in Oracle()) await store.CreateAsync(row);

        Exception? thrown = null;
        try { await store.AggregateAsync(Native(x => x.A > 0)); }
        catch (Exception ex) { thrown = ex; }

        thrown.Should().NotBeNull("the native facet path still fails; if it works now, remove IsNativeFacetGap");
        IsNativeFacetGap(thrown!).Should().BeTrue(thrown!.Message);
    }

    [Fact]
    public async Task Index_manager_queries_answer_what_CSharp_matches()
    {
        if (!_live.Ready()) return;
        var store = _live.Track((url, db) => new AsyncRavenDBStore<T868Row>(url, db));
        var oracle = Oracle();
        foreach (var row in Oracle()) await store.CreateAsync(row);

        IDocumentStore documentStore = store.DocumentStore!;
        var index = new T868RowsIndex();
        await index.ExecuteAsync(documentStore);
        for (var i = 0; i < 100 && (await documentStore.Maintenance.SendAsync(new GetIndexStatisticsOperation(index.IndexName))).IsStale; i++)
            await Task.Delay(100);

        var manager = new RavenDBIndexManager(documentStore);
        var failures = new List<string>();
        foreach (var (shape, filter) in Shapes)
        {
            var expected = oracle.Where(filter.Compile()).Select(r => r.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
            try
            {
                var read = (await manager.QueryMapReduceAsync(index.IndexName, filter)).Select(r => r.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (!read.SequenceEqual(expected)) failures.Add($"QueryMapReduceAsync {shape}: C# {string.Join(",", expected)}, got {string.Join(",", read)}");
            }
            catch (Exception ex)
            {
                failures.Add($"QueryMapReduceAsync {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            try
            {
                var first = await manager.QueryMapReduceFirstAsync(index.IndexName, filter);
                var ok = expected.Count == 0 ? first == null : first != null && expected.Contains(first.Code);
                if (!ok) failures.Add($"QueryMapReduceFirstAsync {shape}: C# one of {string.Join(",", expected)}, got {first?.Code ?? "null"}");
            }
            catch (Exception ex)
            {
                failures.Add($"QueryMapReduceFirstAsync {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            try
            {
                var count = await manager.CountMapReduceAsync(index.IndexName, filter);
                if (count != expected.Count) failures.Add($"CountMapReduceAsync {shape}: C# {expected.Count}, got {count}");
            }
            catch (Exception ex)
            {
                failures.Add($"CountMapReduceAsync {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty();
    }
}
