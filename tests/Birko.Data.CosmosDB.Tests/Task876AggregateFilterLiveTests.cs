using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-876: <c>Aggregate</c> / <c>AggregateAsync</c> emitted no WHERE, so every filter was ignored and the aggregate ran
/// over the whole container, silently (<c>A &gt; 3</c> aggregated all 6 rows, measured by TASK-868). The oracle is C#: the
/// documents as read back, filtered by <c>filter.Compile()</c>, grouped and aggregated with LINQ.
/// </summary>
/// <remarks>
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the emulator); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.
/// </remarks>
public class Task876AggregateFilterLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task876AggregateFilterLiveTests(ITestOutputHelper output) => _output = output;

    public class Sub
    {
        public string? X { get; set; }
    }

    public class AggModel : AbstractModel
    {
        public string? Code { get; set; }
        public string? Grp { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
        public Sub? Sub { get; set; }
    }

    private static List<AggModel> Seed() => new()
    {
        new() { Guid = Guid.NewGuid(), Code = "c1", Grp = "g1", A = 1, Score = 10,   Sub = new() { X = "p" } },
        new() { Guid = Guid.NewGuid(), Code = "c2", Grp = "g1", A = 2, Score = null, Sub = new() { X = "q" } },
        new() { Guid = Guid.NewGuid(), Code = "c3", Grp = "g2", A = 3, Score = 30,   Sub = null },
        new() { Guid = Guid.NewGuid(), Code = "c4", Grp = "g2", A = 4, Score = 40,   Sub = new() { X = "p" } },
        new() { Guid = Guid.NewGuid(), Code = "c5", Grp = "g3", A = 5, Score = null, Sub = new() { X = "p" } },
        new() { Guid = Guid.NewGuid(), Code = "c6", Grp = "g3", A = 6, Score = 60,   Sub = new() { X = "r" } },
    };

    private static (string Label, Expression<Func<AggModel, bool>>? Filter)[] Shapes()
    {
        var codesArray = new[] { "c2", "c4", "c6" };
        var codesList = new List<string> { "c1", "c5" };
        int? nul = null;
        return new (string, Expression<Func<AggModel, bool>>?)[]
        {
            ("noFilter",       null),
            ("constTrue",      x => true),
            ("plain",          x => x.A > 3),
            ("negated",        x => !(x.A > 3)),
            ("compound",       x => x.A >= 2 && (x.Grp == "g1" || x.Score == null)),
            ("containsArray",  x => codesArray.Contains(x.Code!)),
            ("containsList",   x => codesList.Contains(x.Code!)),
            ("notContains",    x => !codesArray.Contains(x.Code!)),
            ("nullOrdering",   x => x.Score > nul),
            ("negNullOrdering", x => !(x.Score > nul) && x.A > 2),
            ("orNullOrdering", x => x.Score > nul || x.A > 4),
            ("nestedMember",   x => x.Sub != null && x.Sub.X == "p"),
            ("eqNull",         x => x.Score == null),
        };
    }

    private static readonly AggregateField[] Aggregates =
    {
        new(AggregateFunction.Count, "A", "cnt"),
        new(AggregateFunction.Sum, "A", "sumA"),
        new(AggregateFunction.Avg, "A", "avgA"),
        new(AggregateFunction.Min, "A", "minA"),
        new(AggregateFunction.Max, "A", "maxA"),
        new(AggregateFunction.Max, "Score", "maxScore"),
    };

    private string? ResolveConnection()
    {
        var conn = Environment.GetEnvironmentVariable(ConnEnv);
        if (!string.IsNullOrWhiteSpace(conn)) return conn;

        const string message = "SKIPPED: no live Cosmos. Set " + ConnEnv + " (and " + ModeEnv
                             + "=Gateway for the emulator); set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return null;
    }

    private static Settings LiveSettings()
    {
        var settings = new Settings();
        if (string.Equals(Environment.GetEnvironmentVariable(ModeEnv), "Gateway", StringComparison.OrdinalIgnoreCase))
        {
            settings.ConnectionMode = ConnectionMode.Gateway;
        }
        return settings;
    }

    /// <summary>
    /// The C# answer: one row per group (one row, keyed "", with no group-by), each aggregate over the group's non-null
    /// values, absent when there are none. An empty match set has no rows when grouped, and one row of
    /// <c>cnt = 0</c> when not, which is what an ungrouped SQL aggregate answers.
    /// </summary>
    private static Dictionary<string, Dictionary<string, double?>> Oracle(
        IReadOnlyList<AggModel> all, Expression<Func<AggModel, bool>>? filter, bool grouped)
    {
        var rows = filter == null ? all.ToList() : all.Where(filter.Compile()).ToList();
        var result = new Dictionary<string, Dictionary<string, double?>>();
        var groups = grouped
            ? rows.GroupBy(r => r.Grp ?? "").ToList()
            : new List<IGrouping<string, AggModel>> { rows.GroupBy(_ => "").FirstOrDefault() ?? new EmptyGrouping() };
        foreach (var g in groups)
        {
            var a = g.Select(r => (double)r.A).ToList();
            var s = g.Where(r => r.Score.HasValue).Select(r => (double)r.Score!.Value).ToList();
            result[g.Key] = new Dictionary<string, double?>
            {
                ["cnt"] = g.Count(),
                ["sumA"] = a.Count > 0 ? a.Sum() : null,
                ["avgA"] = a.Count > 0 ? a.Average() : null,
                ["minA"] = a.Count > 0 ? a.Min() : null,
                ["maxA"] = a.Count > 0 ? a.Max() : null,
                ["maxScore"] = s.Count > 0 ? s.Max() : null,
            };
        }
        return result;
    }

    private sealed class EmptyGrouping : IGrouping<string, AggModel>
    {
        public string Key => "";
        public IEnumerator<AggModel> GetEnumerator() => Enumerable.Empty<AggModel>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The store's answer in the oracle's shape. SUM over no non-null values: Cosmos answers 0 for an empty
    /// group, so a SUM is compared only where the oracle has values (the filter is what is under test, not SUM's identity).</summary>
    private static Dictionary<string, Dictionary<string, double?>> Actual(IReadOnlyList<AggregateResult> results, bool grouped)
    {
        var map = new Dictionary<string, Dictionary<string, double?>>();
        foreach (var r in results)
        {
            var key = grouped ? (r.Values.TryGetValue("Grp", out var k) ? k?.ToString() ?? "" : "<no key>") : "";
            var row = new Dictionary<string, double?>();
            foreach (var agg in Aggregates)
            {
                row[agg.ResolvedAlias] = r.Values.TryGetValue(agg.ResolvedAlias, out var v) && v != null
                    ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                    : null;
            }
            map[key] = row;
        }
        return map;
    }

    private static string? Diff(Dictionary<string, Dictionary<string, double?>> expected, Dictionary<string, Dictionary<string, double?>> actual)
    {
        var keys = expected.Keys.Union(actual.Keys).OrderBy(k => k, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var key in keys)
        {
            if (!expected.TryGetValue(key, out var e)) { sb.Append($"[{key}] unexpected group; "); continue; }
            if (!actual.TryGetValue(key, out var a)) { sb.Append($"[{key}] missing group; "); continue; }
            foreach (var (alias, ev) in e)
            {
                a.TryGetValue(alias, out var av);
                // An empty SUM: C# has no value, Cosmos answers 0 or nothing. Not what this test measures.
                if (ev == null && alias.StartsWith("sum", StringComparison.Ordinal) && (av == null || av == 0)) continue;
                if (ev == null && av == null) continue;
                if (ev != null && av != null && Math.Abs(ev.Value - av.Value) < 1e-9) continue;
                sb.Append($"[{key}] {alias} expected {Fmt(ev)} got {Fmt(av)}; ");
            }
        }
        return sb.Length == 0 ? null : sb.ToString();

        static string Fmt(double? d) => d?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }

    private static AggregateQuery<AggModel> Query(Expression<Func<AggModel, bool>>? filter, bool grouped) => new()
    {
        Filter = filter,
        GroupByFields = grouped ? new[] { "Grp" } : Array.Empty<string>(),
        Aggregates = Aggregates,
    };

    [Fact]
    public async Task AggregateAsync_honours_the_filter_like_CSharp()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t876a_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<AggModel>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(Seed());
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(6);

            var report = new StringBuilder();
            var wrong = 0;
            foreach (var (label, filter) in Shapes())
            {
                foreach (var grouped in new[] { false, true })
                {
                    string line;
                    try
                    {
                        var actual = Actual(await store.AggregateAsync(Query(filter, grouped)), grouped);
                        var diff = Diff(Oracle(all, filter, grouped), actual);
                        if (diff != null) wrong++;
                        line = diff == null ? "OK" : "WRONG " + diff;
                    }
                    catch (Exception e)
                    {
                        wrong++;
                        line = "THROW " + e.GetType().Name + ": " + e.Message.Split('\n')[0];
                    }
                    report.AppendLine($"{label,-16} {(grouped ? "grouped" : "whole  ")} -> {line}");
                }
            }
            _output.WriteLine(report.ToString());
            wrong.Should().Be(0, "every aggregate must see only the rows the filter matches:\n" + report);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Aggregate_sync_honours_the_filter_like_CSharp()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t876s_" + Guid.NewGuid().ToString("N");
        var store = new CosmosDBStore<AggModel>(conn, dbName, null, LiveSettings());
        try
        {
            store.Create(Seed());
            var all = store.Read(x => true).ToList();
            all.Should().HaveCount(6);

            var report = new StringBuilder();
            var wrong = 0;
            foreach (var (label, filter) in Shapes())
            {
                foreach (var grouped in new[] { false, true })
                {
                    string line;
                    try
                    {
                        var actual = Actual(store.Aggregate(Query(filter, grouped)), grouped);
                        var diff = Diff(Oracle(all, filter, grouped), actual);
                        if (diff != null) wrong++;
                        line = diff == null ? "OK" : "WRONG " + diff;
                    }
                    catch (Exception e)
                    {
                        wrong++;
                        line = "THROW " + e.GetType().Name + ": " + e.Message.Split('\n')[0];
                    }
                    report.AppendLine($"{label,-16} {(grouped ? "grouped" : "whole  ")} -> {line}");
                }
            }
            _output.WriteLine(report.ToString());
            wrong.Should().Be(0, "every aggregate must see only the rows the filter matches:\n" + report);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// An aggregate is the first call on a fresh store: it initialises, as every other read does, instead of answering
    /// nothing because the container is not open yet.
    /// </summary>
    [Fact]
    public async Task Aggregate_on_a_fresh_store_initialises_instead_of_answering_nothing()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t876i_" + Guid.NewGuid().ToString("N");
        var writer = new AsyncCosmosDBStore<AggModel>(conn, dbName, null, LiveSettings());
        try
        {
            await writer.CreateAsync(Seed());

            var asyncFresh = new AsyncCosmosDBStore<AggModel>(conn, dbName, null, LiveSettings());
            var asyncRows = await asyncFresh.AggregateAsync(Query(x => x.A > 3, grouped: false));
            asyncRows.Should().ContainSingle();
            asyncRows[0].GetValue<long>("cnt").Should().Be(3);

            var syncFresh = new CosmosDBStore<AggModel>(conn, dbName, null, LiveSettings());
            var syncRows = syncFresh.Aggregate(Query(x => x.A > 3, grouped: false));
            syncRows.Should().ContainSingle();
            syncRows[0].GetValue<long>("cnt").Should().Be(3);
        }
        finally
        {
            try { if (writer.Client != null) await writer.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    private sealed class CountingHandler : RequestHandler
    {
        private int _count;
        public int Count => System.Threading.Volatile.Read(ref _count);
        public override Task<ResponseMessage> SendAsync(RequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class SmallChunkAsyncStore : AsyncCosmosDBStore<AggModel>
    {
        public SmallChunkAsyncStore(Container container) : base(container) { }
        protected override int InListChunkSize => 2;
    }

    private sealed class SmallChunkStore : CosmosDBStore<AggModel>
    {
        public SmallChunkStore(Container container) : base(container) { }
        protected override int InListChunkSize => 2;
    }

    /// <summary>
    /// Groups cannot be merged across IN-list chunks, so an aggregate is one query: past the chunk size but within Cosmos
    /// DB's query-text ceiling it runs and answers right; past the ceiling it is refused by name before any request,
    /// as a sorted read is (TASK-834). Before TASK-876 the list never reached the query at all.
    /// </summary>
    [Fact]
    public async Task An_IN_list_is_one_query_within_the_ceiling_and_refused_past_it()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t876l_" + Guid.NewGuid().ToString("N");
        var handler = new CountingHandler();
        var options = LiveSettings().GetCosmosClientOptions();
        options.CustomHandlers.Add(handler);
        using var client = new CosmosClient(conn, options);
        try
        {
            var database = (await client.CreateDatabaseIfNotExistsAsync(dbName)).Database;
            var container = (await database.CreateContainerIfNotExistsAsync(nameof(AggModel), "/id")).Container;
            await new AsyncCosmosDBStore<AggModel>(container).CreateAsync(Seed());

            var codes = new[] { "c2", "c4", "c6", "missing" };
            var within = new AggregateQuery<AggModel>
            {
                Filter = x => codes.Contains(x.Code!),
                Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt"), new AggregateField(AggregateFunction.Sum, "A", "sumA") },
            };
            var asyncRows = await new SmallChunkAsyncStore(container).AggregateAsync(within);
            asyncRows.Should().ContainSingle();
            asyncRows[0].GetValue<long>("cnt").Should().Be(3);
            asyncRows[0].GetValue<long>("sumA").Should().Be(12);
            var syncRows = new SmallChunkStore(container).Aggregate(within);
            syncRows[0].GetValue<long>("cnt").Should().Be(3);

            var ids = Enumerable.Range(0, 20000).Select(_ => (Guid?)Guid.NewGuid()).ToList();
            var past = new AggregateQuery<AggModel>
            {
                Filter = x => ids.Contains(x.Guid),
                GroupByFields = new[] { "Grp" },
                Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt") },
            };
            var store = new AsyncCosmosDBStore<AggModel>(container);
            var sync = new CosmosDBStore<AggModel>(container);
            var before = handler.Count;
            var ex = (await store.Invoking(s => s.AggregateAsync(past)).Should().ThrowAsync<NotSupportedException>()).Which;
            ex.Message.Should().StartWith("Cosmos DB aggregate of AggModel refused")
                .And.Contain("20000 distinct values")
                .And.Contain("groups and aggregates cannot be combined across chunks")
                .And.NotContain(ids[0].ToString()!);
            sync.Invoking(s => s.Aggregate(past)).Should().Throw<NotSupportedException>().WithMessage("Cosmos DB aggregate of*");
            handler.Count.Should().Be(before, "the refusal is decided before any request");
        }
        finally
        {
            try { await client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}

/// <summary>
/// TASK-876, offline: the aggregate's WHERE is the store's read rendering, carried over verbatim with its parameters, and
/// a rendering that is not a plain WHERE over the container is refused by name, never dropped.
/// </summary>
public class Task876AggregateQueryTests
{
    private static readonly AggregateQuery<Task876AggregateFilterLiveTests.AggModel> CountByGrp = new()
    {
        GroupByFields = new[] { "Grp" },
        Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt"), new AggregateField(AggregateFunction.Sum, "A", "sumA") },
    };

    /// <summary>A container handle for LINQ rendering only; <c>ToQueryDefinition</c> sends nothing.</summary>
    private static Container OfflineContainer()
    {
        var client = new CosmosClient("AccountEndpoint=http://localhost:1/;AccountKey="
            + "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            new Settings().GetCosmosClientOptions());
        return client.GetContainer("db", "c");
    }

    [Fact]
    public void No_filter_renders_no_WHERE()
    {
        var q = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(CountByGrp, null).Query;
        q.QueryText.Should().Be("SELECT root[\"Grp\"] AS \"Grp\", COUNT(1) AS \"cnt\", SUM(IS_NULL(root[\"A\"]) ? undefined : root[\"A\"]) AS \"sumA\" FROM root GROUP BY root[\"Grp\"]");
    }

    [Fact]
    public void The_WHERE_is_the_reads_rendering_between_FROM_and_GROUP_BY()
    {
        var read = OfflineContainer().GetItemLinqQueryable<Task876AggregateFilterLiveTests.AggModel>()
            .Where(x => x.A > 3 && x.Sub != null && x.Sub.X == "p").ToQueryDefinition();
        read.QueryText.Should().StartWith("SELECT VALUE root FROM root WHERE ");
        var predicate = read.QueryText.Substring("SELECT VALUE root FROM root WHERE ".Length);

        var q = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(CountByGrp, read).Query;
        q.QueryText.Should().Be("SELECT root[\"Grp\"] AS \"Grp\", COUNT(1) AS \"cnt\", SUM(IS_NULL(root[\"A\"]) ? undefined : root[\"A\"]) AS \"sumA\" FROM root WHERE "
                                + predicate + " GROUP BY root[\"Grp\"]");
    }

    [Fact]
    public void Every_parameter_of_the_rendering_is_carried()
    {
        var read = new QueryDefinition("SELECT VALUE root FROM root WHERE (root[\"A\"] > @a)").WithParameter("@a", 3);
        var q = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(CountByGrp, read).Query;
        q.QueryText.Should().Contain(" FROM root WHERE (root[\"A\"] > @a) GROUP BY ");
        q.GetQueryParameters().Should().ContainSingle().Which.Should().Be(("@a", (object)3));
    }

    [Fact]
    public void A_rendering_with_no_predicate_is_the_whole_container()
    {
        var q = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(CountByGrp, new QueryDefinition("SELECT VALUE root FROM root")).Query;
        q.QueryText.Should().NotContain("WHERE");
    }

    [Theory]
    [InlineData("SELECT VALUE root FROM root JOIN l IN root[\"Lines\"] WHERE l > 1")]
    [InlineData("SELECT VALUE r0 FROM (SELECT VALUE root FROM root) AS r0 WHERE r0[\"A\"] > 1")]
    [InlineData("SELECT VALUE root FROM root WHERE ")]
    [InlineData("SELECT root FROM root WHERE root[\"A\"] > 1")]
    public void A_rendering_that_is_not_a_plain_WHERE_is_refused_by_name(string text)
    {
        var act = () => Aggregation.CosmosAggregationHelper.BuildAggregateQuery(CountByGrp, new QueryDefinition(text));
        act.Should().Throw<NotSupportedException>().WithMessage("Cosmos DB aggregate of AggModel refused*never dropped*");
    }
}
