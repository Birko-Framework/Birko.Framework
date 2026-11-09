using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Tests.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Aggregation;

public class T868MongoRow : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Code { get; set; }
    public int A { get; set; }
    public string? Grp { get; set; }
    public int? Score { get; set; }
}

/// <summary>
/// TASK-868, live MongoDB: the filter-taking paths TASK-867 did not wire — the stores' <c>Aggregate</c> /
/// <c>AggregateAsync</c> and <see cref="Birko.Data.MongoDB.Aggregation.AggregationPipelineBuilder{T}.Match(Expression{Func{T, bool}})"/>
/// — answer what the compiled C# filter matches. MongoDB's <c>$gte: null</c> matches the null rows, where C#'s lifted
/// ordering against null is false. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public sealed class Task868AggregateNullOrderingLiveTests : IDisposable
{
    private readonly MongoLive _live;
    private readonly ITestOutputHelper _output;

    public Task868AggregateNullOrderingLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new MongoLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static readonly int? Nul = null;
    private static readonly string[] Codes = { "r2", "r5" };

    public static readonly IReadOnlyDictionary<string, Expression<Func<T868MongoRow, bool>>> Shapes =
        new Dictionary<string, Expression<Func<T868MongoRow, bool>>>
        {
            ["Score > nul"] = x => x.Score > Nul,
            ["Score >= nul"] = x => x.Score >= Nul,
            ["nul <= Score"] = x => Nul <= x.Score,
            ["Score <= nul || A > 4"] = x => x.Score <= Nul || x.A > 4,
            ["!(Score > nul) && A > 4"] = x => !(x.Score > Nul) && x.A > 4,
            ["!(Score >= nul || A > 3)"] = x => !(x.Score >= Nul || x.A > 3),
            ["!(Score <= nul && A > 1)"] = x => !(x.Score <= Nul && x.A > 1),
            ["Score != nul"] = x => x.Score != Nul,
            ["codes.Contains(Code)"] = x => Codes.Contains(x.Code),
        };

    private static readonly int?[] Scores = { null, 5, null, 8, null, 2 };

    private static List<T868MongoRow> Oracle() => Scores
        .Select((s, i) => new T868MongoRow { Code = "r" + (i + 1), A = i + 1, Grp = i % 2 == 0 ? "g1" : "g2", Score = s })
        .ToList();

    private static AggregateQuery<T868MongoRow> Query(Expression<Func<T868MongoRow, bool>> filter) => new()
    {
        Filter = filter,
        GroupByFields = new[] { "Grp" },
        Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
    };

    private static string Render(IEnumerable<AggregateResult> results)
        => string.Join(" ; ", results
            .Select(r => r.Values["Grp"] + "=" + Convert.ToDouble(r.Values["n"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))
            .OrderBy(x => x, StringComparer.Ordinal));

    private static string Expected(IEnumerable<T868MongoRow> oracle, Func<T868MongoRow, bool> match)
        => string.Join(" ; ", oracle.Where(match).GroupBy(r => r.Grp)
            .Select(g => g.Key + "=" + g.Count().ToString(CultureInfo.InvariantCulture))
            .OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public async Task Aggregate_and_the_pipeline_match_answer_what_CSharp_matches()
    {
        if (!_live.Ready()) return;
        var asyncStore = new AsyncMongoDBStore<T868MongoRow>();
        asyncStore.SetSettings(_live.NewSettings());
        var syncStore = new MongoDBStore<T868MongoRow>();
        syncStore.SetSettings(_live.NewSettings());
        var oracle = Oracle();
        foreach (var row in Oracle()) await asyncStore.CreateAsync(row);
        foreach (var row in Oracle()) syncStore.Create(row);

        var failures = new List<string>();
        foreach (var (shape, filter) in Shapes)
        {
            var match = filter.Compile();
            var expected = Expected(oracle, match);
            var expectedCodes = oracle.Where(match).Select(r => r.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
            try
            {
                var got = Render(await asyncStore.AggregateAsync(Query(filter)));
                if (got != expected) failures.Add($"AggregateAsync {shape}: C# [{expected}], got [{got}]");
            }
            catch (Exception ex) { failures.Add($"AggregateAsync {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
            try
            {
                var got = Render(syncStore.Aggregate(Query(filter)));
                if (got != expected) failures.Add($"Aggregate {shape}: C# [{expected}], got [{got}]");
            }
            catch (Exception ex) { failures.Add($"Aggregate {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
            try
            {
                var docs = await asyncStore.Aggregate().Match(filter).ToListAsync();
                var codes = docs.Select(d => d["Code"].AsString).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (!codes.SequenceEqual(expectedCodes))
                    failures.Add($"Aggregate().Match {shape}: C# {string.Join(",", expectedCodes)}, got {string.Join(",", codes)}");
            }
            catch (Exception ex) { failures.Add($"Aggregate().Match {shape}: threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty();
    }
}
