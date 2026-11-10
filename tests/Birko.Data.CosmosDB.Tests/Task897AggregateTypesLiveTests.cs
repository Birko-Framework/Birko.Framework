using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-897, live against the Cosmos emulator: a store aggregate answers what <see cref="AggregateHelper.LinqAggregate{T}"/>
/// answers over the same documents as read back, in the same CLR TYPE: group keys and every aggregate of every member
/// type, a DateTime MIN/MAX over mixed fractions of a second and mixed kinds, names in any case; and what it cannot answer
/// so is refused as LINQ refuses it, or by name.
/// </summary>
/// <remarks>
/// <para>Measured before the fix (vnext-preview, 2026-11-10), 12 shapes x 2 modes: every shape differed. Keys and
/// aggregates came back as the JSON held them (<c>long</c> / <c>double</c>, a Guid, a DateTime and an enum key as text
/// or a number); a DateTime MIN/MAX was its stored text and sorted <c>"…00.5Z"</c> before <c>"…00Z"</c>; a group or
/// aggregate named in another case read nothing (every key null, every SUM null); SUM/AVG of a string answered null.</para>
/// <para>The comparison is strict: the same type and the same value (a DateTime by its ticks, as C# compares it).</para>
/// </remarks>
public class Task897AggregateTypesLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task897AggregateTypesLiveTests(ITestOutputHelper output) => _output = output;

    public enum Level { Low = 1, Mid = 5, High = 9 }

    public class Row : AbstractModel
    {
        public string? Grp { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
        public long L { get; set; }
        public double? D { get; set; }
        public decimal? M { get; set; }
        public float F { get; set; }
        public short Sh { get; set; }
        public byte By { get; set; }
        public Level E { get; set; }
        public Level? EN { get; set; }
        public bool B { get; set; }
        public Guid G { get; set; }
        public DateTime T { get; set; }
        public DateTime? TN { get; set; }
        public string? S { get; set; }
    }

    private static DateTime Utc(int hour, int minute, int second, int ms = 0) => new DateTime(2026, 1, 1, hour, minute, second, DateTimeKind.Utc).AddMilliseconds(ms);

    internal static List<Row> Seed() => new()
    {
        // g1: a whole second and the same second and a half — the stored text "…00.5Z" sorts before "…00Z".
        new() { Grp = "g1", A = 1, Score = 10, L = 3_000_000_000L, D = 1.5, M = 0.1m, F = 0.5f, Sh = 3, By = 7, E = Level.Mid, EN = null, B = true,
                G = Guid.Parse("8f000000-0000-0000-0000-000000000001"), T = Utc(10, 0, 0), TN = Utc(10, 0, 0, 500), S = "a" },
        new() { Grp = "g1", A = 2, Score = null, L = 2, D = null, M = 0.2m, F = 1.25f, Sh = -4, By = 200, E = Level.Low, EN = Level.High, B = false,
                G = Guid.Parse("10000000-ffff-0000-0000-000000000000"), T = Utc(10, 0, 0, 500), TN = Utc(10, 0, 0), S = "B" },
        // g2: a value one tick under the next second, and a local time (stored with this machine's offset).
        new() { Grp = "g2", A = 3, Score = 30, L = -5, D = 2.25, M = 1m, F = 2f, Sh = 100, By = 1, E = Level.High, EN = Level.Low, B = true,
                G = Guid.Parse("00000000-0000-0000-0000-0000000000ff"), T = Utc(9, 59, 59).AddTicks(9_999_999), TN = null, S = "c" },
        new() { Grp = "g2", A = 4, Score = 40, L = 7, D = -1, M = 0m, F = -0.75f, Sh = 0, By = 0, E = Level.Low, EN = Level.Mid, B = true,
                G = Guid.NewGuid(), T = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Local), TN = null, S = "d" },
        new() { Grp = "g2", A = 5, Score = null, L = 9, D = 4, M = 0m, F = 4f, Sh = 1, By = 2, E = Level.Mid, EN = null, B = false,
                G = Guid.NewGuid(), T = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local).AddMilliseconds(250), TN = null, S = null },
        // g3: unspecified kinds, with and without a fraction.
        new() { Grp = "g3", A = 6, Score = 60, L = 11, D = 0.125, M = 5m, F = 8f, Sh = 6, By = 6, E = Level.High, EN = Level.High, B = false,
                G = Guid.NewGuid(), T = new DateTime(2026, 1, 1, 8, 0, 0, 250, DateTimeKind.Unspecified), TN = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified), S = "e" },
        new() { Grp = "g3", A = 7, Score = 7, L = 13, D = 3, M = 2.5m, F = 0.25f, Sh = 7, By = 9, E = Level.Low, EN = Level.Low, B = true,
                G = Guid.NewGuid(), T = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified), TN = new DateTime(2026, 1, 1, 7, 59, 59, 999, DateTimeKind.Unspecified), S = "F" },
        new() { Grp = null, A = 8, Score = 8, L = 17, D = 0.5, M = 9.99m, F = 16f, Sh = -1, By = 255, E = Level.Mid, EN = Level.Mid, B = false,
                G = Guid.NewGuid(), T = Utc(23, 59, 59, 999), TN = Utc(0, 0, 0), S = "g" },
    };

    private static readonly string[] Numeric = { "A", "Score", "L", "D", "M", "F", "Sh", "By" };

    /// <summary>Every aggregate LINQ answers over these members, named in another case than the property where noted.</summary>
    private static readonly AggregateField[] Aggregates = Numeric
        .SelectMany(p => new[]
        {
            new AggregateField(AggregateFunction.Sum, p, "sum" + p),
            new AggregateField(AggregateFunction.Avg, p, "avg" + p),
            new AggregateField(AggregateFunction.Min, p, "min" + p),
            new AggregateField(AggregateFunction.Max, p, "max" + p),
        })
        .Concat(new[] { "E", "EN", "B", "G", "T", "TN" }.SelectMany(p => new[]
        {
            new AggregateField(AggregateFunction.Min, p, "min" + p),
            new AggregateField(AggregateFunction.Max, p, "max" + p),
        }))
        .Concat(new[]
        {
            new AggregateField(AggregateFunction.Count, "A", "cnt"),
            // LINQ matches a name in any case.
            new AggregateField(AggregateFunction.Sum, "score", "sumLower"),
            new AggregateField(AggregateFunction.Max, "t", "maxLower"),
        })
        .ToArray();

    private static IEnumerable<(string Label, AggregateQuery<Row> Query)> Cases()
    {
        yield return ("whole", new AggregateQuery<Row> { Aggregates = Aggregates });
        yield return ("by Grp", new AggregateQuery<Row> { GroupByFields = new[] { "Grp" }, Aggregates = Aggregates });
        yield return ("by grp (case)", new AggregateQuery<Row> { GroupByFields = new[] { "grp" }, Aggregates = Aggregates });
        yield return ("by GRP, filtered", new AggregateQuery<Row> { Filter = x => x.A > 2, GroupByFields = new[] { "GRP" }, Aggregates = Aggregates });
        foreach (var key in new[] { "E", "EN", "B", "A", "Score", "L", "D", "M", "G", "T" })
        {
            yield return ($"by {key}", new AggregateQuery<Row> { GroupByFields = new[] { key }, Aggregates = Aggregates });
        }
        yield return ("by By, e (case)", new AggregateQuery<Row> { GroupByFields = new[] { "By", "e" }, Aggregates = Aggregates });
    }

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

    private static string Fmt(object? v) => v switch
    {
        null => "null",
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture) + ":DateTime",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) + ":" + v.GetType().Name,
        _ => v + ":" + v.GetType().Name,
    };

    private static string KeyOf(AggregateResult row, AggregateQuery<Row> query)
        => string.Join("|", query.GroupByFields.Select(f => row.Values.TryGetValue(f, out var v)
            ? v is DateTime dt ? dt.Ticks.ToString(CultureInfo.InvariantCulture) : v?.ToString() ?? "<null>"
            : "<absent>"));

    /// <summary>The same type and the same value; a DateTime by its ticks, as <see cref="DateTime.Equals(DateTime)"/>.</summary>
    private static bool Same(object? expected, object? actual)
    {
        if (expected == null || actual == null) return expected == null && actual == null;
        if (expected.GetType() != actual.GetType()) return false;
        return expected.Equals(actual);
    }

    internal static string? Diff(IReadOnlyList<AggregateResult> expected, IReadOnlyList<AggregateResult> actual, AggregateQuery<Row> query)
    {
        var e = expected.ToDictionary(r => KeyOf(r, query));
        var a = new Dictionary<string, AggregateResult>();
        var sb = new StringBuilder();
        foreach (var row in actual)
        {
            var key = KeyOf(row, query);
            if (!a.TryAdd(key, row)) sb.Append($"[{key}] answered twice; ");
        }
        foreach (var key in e.Keys.Union(a.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!e.TryGetValue(key, out var er)) { sb.Append($"[{key}] unexpected row; "); continue; }
            if (!a.TryGetValue(key, out var ar)) { sb.Append($"[{key}] missing row; "); continue; }
            foreach (var (name, ev) in er.Values)
            {
                if (!ar.Values.TryGetValue(name, out var av)) { sb.Append($"[{key}] {name} absent (C# {Fmt(ev)}); "); continue; }
                if (!Same(ev, av)) sb.Append($"[{key}] {name} C# {Fmt(ev)} got {Fmt(av)}; ");
            }
            foreach (var name in ar.Values.Keys.Except(er.Values.Keys)) sb.Append($"[{key}] extra column {name}; ");
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private async Task RunMatrix(Func<AggregateQuery<Row>, Task<IReadOnlyList<AggregateResult>>> aggregate, IReadOnlyList<Row> all)
    {
        var report = new StringBuilder();
        var wrong = 0;
        var cases = Cases().ToList();
        foreach (var (label, query) in cases)
        {
            var expected = AggregateHelper.LinqAggregate(all, query);
            string line;
            try
            {
                var diff = Diff(expected, await aggregate(query), query);
                if (diff != null) wrong++;
                line = diff == null ? "OK" : "WRONG " + diff;
            }
            catch (Exception ex)
            {
                wrong++;
                line = "THROW " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
            }
            report.AppendLine($"{label,-20} -> {line}");
        }
        _output.WriteLine(report.ToString());
        wrong.Should().Be(0, $"every aggregate must answer what LINQ answers, in its type ({wrong} of {cases.Count} differ):\n" + report);
    }

    [Fact]
    public async Task AggregateAsync_answers_LINQs_values_in_LINQs_types()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t897a_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(Seed());
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(8);
            await RunMatrix(q => store.AggregateAsync(q), all);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Aggregate_sync_answers_LINQs_values_in_LINQs_types()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t897s_" + Guid.NewGuid().ToString("N");
        var store = new CosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            store.Create(Seed());
            var all = store.Read(x => true).ToList();
            all.Should().HaveCount(8);
            await RunMatrix(q => Task.FromResult(store.Aggregate(q)), all);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// A decimal SUM is computed by Cosmos DB in double and read back to 15 significant digits; an AVG of a decimal is that
    /// SUM divided by the count in C# decimal, as LINQ divides it (Cosmos DB's own AVG is a double: 1/3 would read
    /// 0.333333333333333 where C# answers 28 digits).
    /// </summary>
    [Fact]
    public async Task A_decimal_average_is_divided_in_decimal()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t897d_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(new List<Row>
            {
                new() { Grp = "x", M = 1m, T = Utc(1, 0, 0) },
                new() { Grp = "x", M = 0m, T = Utc(1, 0, 0) },
                new() { Grp = "x", M = 0m, T = Utc(1, 0, 0) },
                new() { Grp = "y", M = 0.1m, T = Utc(1, 0, 0) },
                new() { Grp = "y", M = 0.2m, T = Utc(1, 0, 0) },
                new() { Grp = "z", M = null, T = Utc(1, 0, 0) },
            });
            var all = (await store.ReadAsync(x => true)).ToList();
            var query = new AggregateQuery<Row>
            {
                GroupByFields = new[] { "Grp" },
                Aggregates = new[] { new AggregateField(AggregateFunction.Avg, "M", "avg"), new AggregateField(AggregateFunction.Sum, "M", "sum") },
            };
            var diff = Diff(AggregateHelper.LinqAggregate(all, query), await store.AggregateAsync(query), query);
            diff.Should().BeNull();
            (await store.AggregateAsync(query)).Single(r => (string?)r.Values["Grp"] == "x").Values["avg"].Should().Be(1m / 3m);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
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

    /// <summary>
    /// What LINQ refuses is refused with its exception and message (SUM/AVG of a non-numeric member, a name that is no
    /// property); what Cosmos DB cannot order as C# does is refused by name (MIN/MAX of a string: Cosmos DB orders by code
    /// point, C# by culture). All before any request, on both stores.
    /// </summary>
    [Fact]
    public async Task What_it_cannot_answer_as_LINQ_is_refused_before_any_request()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t897r_" + Guid.NewGuid().ToString("N");
        var handler = new CountingHandler();
        var options = LiveSettings().GetCosmosClientOptions();
        options.CustomHandlers.Add(handler);
        using var client = new CosmosClient(conn, options);
        try
        {
            var database = (await client.CreateDatabaseIfNotExistsAsync(dbName)).Database;
            var container = (await database.CreateContainerIfNotExistsAsync(nameof(Row), "/id")).Container;
            var store = new AsyncCosmosDBStore<Row>(container);
            var sync = new CosmosDBStore<Row>(container);
            await store.CreateAsync(Seed());
            var all = (await store.ReadAsync(x => true)).ToList();

            AggregateQuery<Row> Q(AggregateFunction f, string member) => new() { Aggregates = new[] { new AggregateField(f, member, "x") } };
            var linqRefused = new[]
            {
                Q(AggregateFunction.Sum, "S"), Q(AggregateFunction.Avg, "S"), Q(AggregateFunction.Sum, "B"), Q(AggregateFunction.Avg, "E"),
                Q(AggregateFunction.Sum, "G"), Q(AggregateFunction.Avg, "T"), Q(AggregateFunction.Sum, "TN"),
                Q(AggregateFunction.Min, "Nope"),
                new AggregateQuery<Row> { GroupByFields = new[] { "Nope" }, Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") } },
            };
            var cosmosRefused = new[] { Q(AggregateFunction.Min, "S"), Q(AggregateFunction.Max, "s") };

            var before = handler.Count;
            foreach (var query in linqRefused)
            {
                var linq = FluentActions.Invoking(() => AggregateHelper.LinqAggregate(all, query)).Should().Throw<Exception>().Which;
                var ex = await FluentActions.Invoking(() => store.AggregateAsync(query)).Should().ThrowAsync<Exception>();
                ex.Which.GetType().Should().Be(linq.GetType());
                ex.Which.Message.Should().Be(linq.Message);
                FluentActions.Invoking(() => sync.Aggregate(query)).Should().Throw<Exception>().Which.Message.Should().Be(linq.Message);
            }
            foreach (var query in cosmosRefused)
            {
                (await FluentActions.Invoking(() => store.AggregateAsync(query)).Should().ThrowAsync<NotSupportedException>())
                    .WithMessage("Cosmos DB aggregate of Row refused: Min/Max of Row.S, a String,*TASK-897*");
                FluentActions.Invoking(() => sync.Aggregate(query)).Should().Throw<NotSupportedException>();
            }
            handler.Count.Should().Be(before, "each refusal is decided before any request");
        }
        finally
        {
            try { await client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// The measurement behind the string refusal: Cosmos DB's MIN of a string is ordinal ("B" before "a"), LINQ's is the
    /// culture's ("a" before "B"). Asked of the server directly, since the store now refuses it.
    /// </summary>
    [Fact]
    public async Task Cosmos_orders_strings_by_code_point_where_LINQ_orders_by_culture()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t897o_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(Seed());
            var all = (await store.ReadAsync(x => true)).ToList();
            var linq = AggregateHelper.LinqAggregate(all, new AggregateQuery<Row> { Aggregates = new[] { new AggregateField(AggregateFunction.Min, "S", "m") } });
            linq[0].Values["m"].Should().Be("a");

            using var iterator = store.Container!.GetItemQueryIterator<System.Text.Json.JsonElement>(
                new QueryDefinition("SELECT MIN(c[\"S\"]) AS m FROM c WHERE NOT IS_NULL(c[\"S\"])"));
            var server = (await iterator.ReadNextAsync()).Single().GetProperty("m").GetString();
            server.Should().Be("B");
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}

/// <summary>TASK-897, offline: the statement a store aggregate sends, and what it refuses before sending anything.</summary>
public class Task897AggregateQueryTests
{
    private static string Text(params AggregateField[] fields)
        => Aggregation.CosmosAggregationHelper.BuildAggregateQuery(new AggregateQuery<Task897AggregateTypesLiveTests.Row> { Aggregates = fields }, null).Query.QueryText;

    [Fact]
    public void A_name_in_another_case_is_sent_as_the_property_name()
    {
        var text = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(new AggregateQuery<Task897AggregateTypesLiveTests.Row>
        {
            GroupByFields = new[] { "grp" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Max, "score", "m") },
        }, null).Query.QueryText;
        text.Should().Be("SELECT root[\"Grp\"] AS \"grp\", MAX(IS_NULL(root[\"Score\"]) ? undefined : root[\"Score\"]) AS \"m\" FROM root GROUP BY root[\"Grp\"]");
    }

    [Fact]
    public void A_DateTime_MIN_compares_the_padded_wall_clock()
    {
        Text(new AggregateField(AggregateFunction.Min, "T", "m")).Should().Contain(
            "MIN(IS_NULL(root[\"T\"]) ? undefined : CONCAT(LEFT(root[\"T\"], 19), \".\", LEFT(CONCAT(");
    }

    [Fact]
    public void A_decimal_AVG_is_a_hidden_SUM_and_COUNT()
    {
        const string v = "IS_NULL(root[\"M\"]) ? undefined : root[\"M\"]";
        Text(new AggregateField(AggregateFunction.Avg, "M", "a")).Should().Be(
            $"SELECT SUM({v}) AS \"__birko_n2\", COUNT({v}) AS \"__birko_n3\" FROM root");
    }

    [Fact]
    public void A_DateTime_group_is_grouped_on_its_wall_clock()
    {
        var text = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(new AggregateQuery<Task897AggregateTypesLiveTests.Row>
        {
            GroupByFields = new[] { "t" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
        }, null).Query.QueryText;
        text.Should().StartWith("SELECT COUNT(1) AS \"n\", MIN(IS_NULL(root[\"T\"]) ? undefined : CONCAT(LEFT(root[\"T\"], 19)")
            .And.Contain("AS \"t\" FROM root GROUP BY CONCAT(LEFT(root[\"T\"], 19)");
    }

    [Theory]
    [InlineData(AggregateFunction.Sum, "S", typeof(NotSupportedException), "Sum is not defined for String.")]
    [InlineData(AggregateFunction.Avg, "B", typeof(NotSupportedException), "Avg is not defined for Boolean.")]
    [InlineData(AggregateFunction.Sum, "E", typeof(NotSupportedException), "Sum is not defined for Level.")]
    [InlineData(AggregateFunction.Avg, "TN", typeof(NotSupportedException), "Avg is not defined for DateTime.")]
    [InlineData(AggregateFunction.Min, "S", typeof(NotSupportedException), "Cosmos DB aggregate of Row refused: Min/Max of Row.S, a String,*TASK-897*")]
    [InlineData(AggregateFunction.Max, "nope", typeof(ArgumentException), "Aggregate aggregate source 'nope' is not a property of Row.*")]
    public void What_LINQ_or_Cosmos_cannot_answer_is_refused_by_name(AggregateFunction function, string member, Type exception, string message)
    {
        FluentActions.Invoking(() => Text(new AggregateField(function, member, "x")))
            .Should().Throw<Exception>().Where(e => e.GetType() == exception).WithMessage(message);
    }
}
