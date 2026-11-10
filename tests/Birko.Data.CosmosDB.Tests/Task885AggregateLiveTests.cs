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
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-885, live against the Cosmos emulator: a store aggregate answers what <see cref="AggregateHelper.LinqAggregate{T}"/>
/// answers over the same documents as read back, for every aggregate kind over nullable and non-nullable members, grouped
/// and not, filtered and not, and with a time bucket; what it cannot answer so is refused by name.
/// </summary>
/// <remarks>
/// <para>Measured before the fix (vnext-preview, 2026-11-10): SUM, AVG and MIN over a member holding a null answered null
/// (SUM(Score) where C# gives 140, MIN 10, AVG 35; also decimal, double, string MIN, DateTime MIN), and a time bucket was
/// ignored: one row where C# has five buckets.</para>
/// <para>Not compared here, TASK-886's: an ungrouped aggregate over no rows (C# answers no row, Cosmos DB one).
/// <see cref="An_ungrouped_aggregate_over_no_rows_is_one_row_of_count_zero_and_nulls"/> records what Cosmos DB answers.</para>
/// <para>Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the emulator); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.</para>
/// </remarks>
public class Task885AggregateLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task885AggregateLiveTests(ITestOutputHelper output) => _output = output;

    public class AggRow : AbstractModel
    {
        public string? Grp { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
        public double? D { get; set; }
        public decimal? M { get; set; }
        public string? S { get; set; }
        public DateTime T { get; set; }
        public DateTime? TN { get; set; }
    }

    private static DateTime Utc(int day, int hour, int minute, int second = 0)
        => new(2026, 1, day, hour, minute, second, DateTimeKind.Utc);

    private static List<AggRow> Seed() => new()
    {
        new() { Guid = Guid.NewGuid(), Grp = "g1", A = 1, Score = 10,   D = 1.5,  M = 1.25m, S = "b",  T = Utc(1, 10, 5),       TN = null },
        new() { Guid = Guid.NewGuid(), Grp = "g1", A = 2, Score = null, D = null, M = null,  S = null, T = Utc(1, 10, 50, 30),  TN = Utc(1, 11, 0) },
        new() { Guid = Guid.NewGuid(), Grp = "g2", A = 3, Score = 30,   D = 2.5,  M = 2.5m,  S = "a",  T = Utc(1, 11, 5),       TN = Utc(1, 11, 20) },
        new() { Guid = Guid.NewGuid(), Grp = "g2", A = 4, Score = 40,   D = -1,   M = 3m,    S = "c",  T = Utc(1, 12, 0),       TN = null },
        new() { Guid = Guid.NewGuid(), Grp = "g3", A = 5, Score = null, D = null, M = null,  S = null, T = Utc(2, 0, 0),        TN = null },
        new() { Guid = Guid.NewGuid(), Grp = "g3", A = 6, Score = 60,   D = 4,    M = 5m,    S = "d",  T = Utc(2, 0, 30, 15),   TN = Utc(2, 0, 10) },
        new() { Guid = Guid.NewGuid(), Grp = "g4", A = 7, Score = null, D = null, M = null,  S = null, T = Utc(3, 0, 30),       TN = null },
        new() { Guid = Guid.NewGuid(), Grp = null, A = 8, Score = 8,    D = 0.25, M = 0.1m,  S = "e",  T = Utc(3, 23, 59, 59),  TN = null },
        // A local time is bucketed on its wall clock, as in memory (the document stores it with this machine's offset).
        new() { Guid = Guid.NewGuid(), Grp = "g5", A = 9, Score = 9,    D = 9,    M = 9m,    S = "f",  T = new DateTime(2026, 1, 4, 10, 30, 0, DateTimeKind.Local), TN = null },
    };

    private static readonly AggregateField[] Aggregates = new[] { "Score", "D", "M", "A" }
        .SelectMany(p => new[]
        {
            new AggregateField(AggregateFunction.Sum, p, "sum" + p),
            new AggregateField(AggregateFunction.Avg, p, "avg" + p),
            new AggregateField(AggregateFunction.Min, p, "min" + p),
            new AggregateField(AggregateFunction.Max, p, "max" + p),
        })
        .Concat(new[]
        {
            new AggregateField(AggregateFunction.Min, "S", "minS"),
            new AggregateField(AggregateFunction.Max, "S", "maxS"),
            new AggregateField(AggregateFunction.Min, "TN", "minTN"),
            new AggregateField(AggregateFunction.Max, "TN", "maxTN"),
            new AggregateField(AggregateFunction.Count, "A", "cnt"),
        })
        .ToArray();

    private static (string Label, Expression<Func<AggRow, bool>>? Filter)[] Filters()
    {
        int? nul = null;
        return new (string, Expression<Func<AggRow, bool>>?)[]
        {
            ("none", null),
            ("A>2", x => x.A > 2),
            ("Score==null", x => x.Score == null),
            ("!(A>3)", x => !(x.A > 3)),
            ("allNullGroup", x => x.Grp == "g4"),
            ("nullOrdering", x => x.Score > nul),
        };
    }

    private static IEnumerable<(string Label, AggregateQuery<AggRow> Query)> Cases()
    {
        foreach (var (label, filter) in Filters())
        {
            yield return (label + " whole", new AggregateQuery<AggRow> { Filter = filter, Aggregates = Aggregates });
            yield return (label + " byGrp", new AggregateQuery<AggRow> { Filter = filter, GroupByFields = new[] { "Grp" }, Aggregates = Aggregates });
        }

        var small = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt"), new AggregateField(AggregateFunction.Sum, "Score", "sumScore"), new AggregateField(AggregateFunction.Min, "Score", "minScore") };
        foreach (var interval in new[] { "1 hour", "15 minutes", "1 day", "00:00:30", "2 hours" })
        {
            yield return ($"bucket {interval} T", new AggregateQuery<AggRow> { TimeBucketInterval = interval, TimeColumn = "T", Aggregates = small });
            yield return ($"bucket {interval} T byGrp", new AggregateQuery<AggRow> { TimeBucketInterval = interval, TimeColumn = "T", GroupByFields = new[] { "Grp" }, Aggregates = small });
        }
        yield return ("bucket 1 hour TN", new AggregateQuery<AggRow> { TimeBucketInterval = "1 hour", TimeColumn = "TN", Aggregates = small });
        yield return ("bucket 1 hour t (case)", new AggregateQuery<AggRow> { TimeBucketInterval = "1 hour", TimeColumn = "t", Aggregates = small });
        yield return ("bucket 1 hour T, A>2", new AggregateQuery<AggRow> { Filter = x => x.A > 2, TimeBucketInterval = "1 hour", TimeColumn = "T", Aggregates = small });
        yield return ("bucket only, no aggregates", new AggregateQuery<AggRow> { TimeBucketInterval = "1 day", TimeColumn = "T" });
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

    private static string KeyOf(AggregateResult row, AggregateQuery<AggRow> query)
    {
        var parts = query.GroupByFields.Select(f => row.Values.TryGetValue(f, out var v) ? v?.ToString() ?? "<null>" : "<absent>").ToList();
        if (query.TimeColumn != null)
        {
            parts.Add(row.Values.TryGetValue("bucket_time", out var b)
                ? b is DateTime dt ? dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : b?.ToString() ?? "<null>"
                : "<absent>");
        }
        return string.Join("|", parts);
    }

    private static bool Same(object? expected, object? actual)
    {
        if (expected == null || actual == null) return expected == null && actual == null;
        if (expected is DateTime e)
        {
            // MIN / MAX of a DateTime comes back as its stored text (a recorded type gap, not this task's): the instant is
            // compared.
            if (actual is DateTime a) return e.Ticks == a.Ticks;
            return actual is string s && DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime() == e.ToUniversalTime();
        }
        if (expected is string es) return actual is string acs && es == acs;
        return Math.Abs(Convert.ToDouble(expected, CultureInfo.InvariantCulture) - Convert.ToDouble(actual, CultureInfo.InvariantCulture)) < 1e-9;
    }

    private static string? Diff(IReadOnlyList<AggregateResult> expected, IReadOnlyList<AggregateResult> actual, AggregateQuery<AggRow> query)
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

        static string Fmt(object? v) => v switch
        {
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
    }

    private async Task RunMatrix(Func<AggregateQuery<AggRow>, Task<IReadOnlyList<AggregateResult>>> aggregate, IReadOnlyList<AggRow> all)
    {
        var report = new StringBuilder();
        var wrong = 0;
        var cases = Cases().ToList();
        foreach (var (label, query) in cases)
        {
            var expected = AggregateHelper.LinqAggregate(all, query);
            if (expected.Count == 0 && query.GroupByFields.Count == 0 && query.TimeColumn == null)
            {
                report.AppendLine($"{label,-32} -> skipped: no rows, TASK-886's");
                continue;
            }

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
            report.AppendLine($"{label,-32} -> {line}");
        }
        _output.WriteLine(report.ToString());
        wrong.Should().Be(0, $"every aggregate must answer what LINQ answers ({wrong} of {cases.Count} differ):\n" + report);
    }

    [Fact]
    public async Task AggregateAsync_answers_what_LINQ_answers()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t885a_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<AggRow>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(Seed());
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(9);
            await RunMatrix(q => store.AggregateAsync(q), all);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Aggregate_sync_answers_what_LINQ_answers()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t885s_" + Guid.NewGuid().ToString("N");
        var store = new CosmosDBStore<AggRow>(conn, dbName, null, LiveSettings());
        try
        {
            store.Create(Seed());
            var all = store.Read(x => true).ToList();
            all.Should().HaveCount(9);
            await RunMatrix(q => Task.FromResult(store.Aggregate(q)), all);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Recorded, not decided (TASK-886 owns the cross-provider answer): over no rows an ungrouped aggregate is one row, its
    /// count 0 and every other aggregate null. SUM was 0 before TASK-885; it is null now, as a SUM over no non-null value
    /// is in a group.
    /// </summary>
    [Fact]
    public async Task An_ungrouped_aggregate_over_no_rows_is_one_row_of_count_zero_and_nulls()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t885e_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<AggRow>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(Seed());
            var rows = await store.AggregateAsync(new AggregateQuery<AggRow> { Filter = x => x.A > 100, Aggregates = Aggregates });
            rows.Should().ContainSingle();
            rows[0].GetValue<long>("cnt").Should().Be(0);
            foreach (var aggregate in Aggregates.Where(a => a.Function != AggregateFunction.Count))
            {
                rows[0].Values.Should().ContainKey(aggregate.ResolvedAlias).WhoseValue.Should().BeNull(aggregate.ResolvedAlias);
            }
            rows[0].Values.Keys.Should().NotContain(k => k.StartsWith("__birko", StringComparison.Ordinal));
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
    /// A time bucket Cosmos DB cannot answer as the in-memory aggregation does is refused by name before any request: an
    /// interval that does not divide the time from 0001-01-01 to 1970-01-01, a fraction of a second, a non-DateTime column;
    /// and, as in memory, a column that is not a property, an interval that does not parse, one half of the pair.
    /// </summary>
    [Fact]
    public async Task A_time_bucket_it_cannot_answer_is_refused_before_any_request()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t885r_" + Guid.NewGuid().ToString("N");
        var handler = new CountingHandler();
        var options = LiveSettings().GetCosmosClientOptions();
        options.CustomHandlers.Add(handler);
        using var client = new CosmosClient(conn, options);
        try
        {
            var database = (await client.CreateDatabaseIfNotExistsAsync(dbName)).Database;
            var container = (await database.CreateContainerIfNotExistsAsync(nameof(AggRow), "/id")).Container;
            var store = new AsyncCosmosDBStore<AggRow>(container);
            var sync = new CosmosDBStore<AggRow>(container);
            await store.CreateAsync(Seed());

            var count = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt") };
            var refused = new (AggregateQuery<AggRow> Query, Type Exception, string Message)[]
            {
                (new() { TimeBucketInterval = "7 days", TimeColumn = "T", Aggregates = count }, typeof(NotSupportedException), "*'7 days' does not divide the time from 0001-01-01 to 1970-01-01*"),
                (new() { TimeBucketInterval = "00:00:00.500", TimeColumn = "T", Aggregates = count }, typeof(NotSupportedException), "*not a whole number of seconds*"),
                (new() { TimeBucketInterval = "1 hour", TimeColumn = "S", Aggregates = count }, typeof(NotSupportedException), "*time column 'S' is String; only a DateTime member*"),
                (new() { TimeBucketInterval = "1 hour", TimeColumn = "Nope", Aggregates = count }, typeof(ArgumentException), "*'Nope' is not a property of AggRow*"),
                (new() { TimeBucketInterval = "bogus", TimeColumn = "T", Aggregates = count }, typeof(ArgumentException), "*'bogus' is not a positive interval*"),
                (new() { TimeBucketInterval = "1 hour", Aggregates = count }, typeof(ArgumentException), "*needs both*"),
                (new() { TimeColumn = "T", Aggregates = count }, typeof(ArgumentException), "*needs both*"),
            };

            var before = handler.Count;
            foreach (var (query, type, message) in refused)
            {
                // An ArgumentException is the in-memory aggregation's own refusal, of the same query; a NotSupportedException
                // is what Cosmos DB cannot answer as it does.
                if (type == typeof(ArgumentException))
                {
                    FluentActions.Invoking(() => AggregateHelper.LinqAggregate(Seed(), query)).Should().Throw<ArgumentException>();
                }

                var ex = await FluentActions.Invoking(() => store.AggregateAsync(query)).Should().ThrowAsync<Exception>();
                ex.Which.Should().BeOfType(type);
                ex.Which.Message.Should().Match(message);
                FluentActions.Invoking(() => sync.Aggregate(query)).Should().Throw<Exception>().Which.Should().BeOfType(type);
            }
            handler.Count.Should().Be(before, "each refusal is decided before any request");
        }
        finally
        {
            try { await client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Cosmos DB's date functions answer nothing before 1601-01-01 (measured, DateTime.MinValue included), so such a row
    /// has no bucket. It is refused by name rather than answered as the null bucket; a null time still is the null bucket.
    /// </summary>
    [Fact]
    public async Task A_time_Cosmos_cannot_bucket_is_refused_not_answered_as_the_null_bucket()
    {
        var conn = ResolveConnection();
        if (conn == null) return;

        var dbName = "birko_t885m_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<AggRow>(conn, dbName, null, LiveSettings());
        try
        {
            await store.CreateAsync(new List<AggRow>
            {
                new() { Guid = Guid.NewGuid(), Grp = "x", A = 1, T = DateTime.MinValue, TN = null },
                new() { Guid = Guid.NewGuid(), Grp = "x", A = 2, T = Utc(1, 10, 0), TN = new DateTime(1500, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            });
            var count = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt") };

            var ex = await FluentActions.Invoking(() => store.AggregateAsync(new AggregateQuery<AggRow> { TimeBucketInterval = "1 hour", TimeColumn = "T", Aggregates = count }))
                .Should().ThrowAsync<NotSupportedException>();
            ex.Which.Message.Should().Match("Cosmos DB aggregate of AggRow refused: 1 value(s) of time column 'T' could not be bucketed*1601-01-01*");

            await FluentActions.Invoking(() => store.AggregateAsync(new AggregateQuery<AggRow> { TimeBucketInterval = "1 hour", TimeColumn = "TN", Aggregates = count }))
                .Should().ThrowAsync<NotSupportedException>().WithMessage("*time column 'TN' could not be bucketed*");

            // Without the pre-1601 row, the null TN is the null bucket, as in memory.
            var rows = await store.AggregateAsync(new AggregateQuery<AggRow> { Filter = x => x.A == 1, TimeBucketInterval = "1 hour", TimeColumn = "TN", Aggregates = count });
            rows.Should().ContainSingle();
            rows[0].Values["bucket_time"].Should().BeNull();
            rows[0].GetValue<long>("cnt").Should().Be(1);
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}

/// <summary>TASK-885, offline: the statement a store aggregate sends.</summary>
public class Task885AggregateQueryTests
{
    private static readonly AggregateField[] Fields =
    {
        new(AggregateFunction.Sum, "Score", "s"),
        new(AggregateFunction.Avg, "Score", "a"),
        new(AggregateFunction.Min, "Score", "mi"),
        new(AggregateFunction.Max, "Score", "ma"),
        new(AggregateFunction.Count, "A", "n"),
    };

    [Fact]
    public void Every_aggregate_skips_nulls_and_a_SUM_counts_what_it_saw()
    {
        var plan = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(
            new AggregateQuery<Task885AggregateLiveTests.AggRow> { Aggregates = Fields }, null);
        const string v = "IS_NULL(root[\"Score\"]) ? undefined : root[\"Score\"]";
        plan.Query.QueryText.Should().Be(
            $"SELECT SUM({v}) AS \"s\", AVG({v}) AS \"a\", MIN({v}) AS \"mi\", MAX({v}) AS \"ma\", COUNT(1) AS \"n\", "
            + $"COUNT({v}) AS \"__birko_n6\" FROM root");
    }

    [Fact]
    public void A_time_bucket_is_grouped_on_the_wall_clock_floored_from_the_epoch()
    {
        var plan = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(
            new AggregateQuery<Task885AggregateLiveTests.AggRow>
            {
                GroupByFields = new[] { "Grp" },
                TimeBucketInterval = "15 minutes",
                TimeColumn = "t",
                Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "n") },
            }, null);
        const string bucket = "FLOOR(DateTimeToTimestamp(LEFT(root[\"T\"], 19)) / 900000) * 900000";
        plan.Query.QueryText.Should().Be(
            $"SELECT root[\"Grp\"] AS \"Grp\", COUNT(1) AS \"n\", {bucket} AS \"bucket_time\", "
            + "COUNT(IS_NULL(root[\"T\"]) ? undefined : root[\"T\"]) AS \"__birko_n3\" FROM root "
            + $"GROUP BY root[\"Grp\"], {bucket}");
    }

    [Fact]
    public void A_hidden_column_never_takes_a_name_the_query_uses()
    {
        var plan = Aggregation.CosmosAggregationHelper.BuildAggregateQuery(
            new AggregateQuery<Task885AggregateLiveTests.AggRow>
            {
                Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "A", "__birko_n2"), new AggregateField(AggregateFunction.Sum, "A", "__birko_n3") },
            }, null);
        plan.Query.QueryText.Should().Contain("AS \"__birko_n3_\"").And.Contain("AS \"__birko_n4\"");
    }
}
