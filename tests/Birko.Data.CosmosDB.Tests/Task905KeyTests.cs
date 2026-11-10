using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>TASK-905, offline: the TimeSpan magnitude key and what the store's LINQ provider is handed.</summary>
public class Task905TimeSpanKeyTests
{
    private static readonly CosmosClient Client = new("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");

    private static IQueryable<Task905TimeSpanLiveTests.Row> Queryable() => Client.GetContainer("db", "c").GetItemLinqQueryable<Task905TimeSpanLiveTests.Row>();

    private static string Where(Expression<Func<Task905TimeSpanLiveTests.Row, bool>> filter)
    {
        var text = Queryable().Where(CosmosFilterRewrite.Prepare(filter)!).ToQueryDefinition().QueryText;
        return text.Substring(text.IndexOf(" WHERE ", StringComparison.Ordinal) + 7);
    }

    private static readonly TimeSpan[] Values =
    {
        TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromTicks(-1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5),
        TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(-0.5), TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(-2),
        TimeSpan.FromHours(23), new(1, 2, 3, 4), TimeSpan.FromDays(2), TimeSpan.FromDays(10), TimeSpan.FromDays(-1),
        new(-1, -2, -3, -4), TimeSpan.FromDays(9_999_999), TimeSpan.MaxValue, TimeSpan.MinValue,
        TimeSpan.MaxValue - TimeSpan.FromTicks(1), TimeSpan.MinValue + TimeSpan.FromTicks(1),
    };

    [Fact]
    public void The_sign_and_the_magnitude_key_order_every_pair_as_CSharp()
    {
        // The sign is the text's first character ("-" below every digit); within a sign the key orders the magnitude.
        static int Ordered(TimeSpan a, TimeSpan b)
        {
            var na = string.CompareOrdinal(a.ToString("c"), "0") < 0;
            var nb = string.CompareOrdinal(b.ToString("c"), "0") < 0;
            if (na != nb) return na ? -1 : 1;
            var c = Math.Sign(string.CompareOrdinal(CosmosFilterRewrite.TimeSpanKeyOf(a), CosmosFilterRewrite.TimeSpanKeyOf(b)));
            return na ? -c : c;
        }

        foreach (var a in Values)
        foreach (var b in Values)
        {
            Ordered(a, b).Should().Be(a.CompareTo(b), $"{a:c} vs {b:c}");
        }
        CosmosFilterRewrite.TimeSpanKeyOf(new TimeSpan(1, 2, 3, 4)).Should().Be("0000000102:03:040000000");
        CosmosFilterRewrite.TimeSpanKeyOf(TimeSpan.MinValue).Should().Be("1067519902:48:054775808");
        CosmosFilterRewrite.TimeSpanKeyOf(TimeSpan.FromSeconds(-0.5)).Should().Be("0000000000:00:005000000");
    }

    [Fact]
    public void An_ordering_is_split_by_sign_and_equality_keeps_the_text()
    {
        var h23 = TimeSpan.FromHours(23);
        var m1 = TimeSpan.FromSeconds(-1);
        Where(x => x.D > h23).Should().StartWith(
                "((root[\"D\"] >= \"0\") AND (CONCAT(CONCAT(SUBSTRING(\"0000000000\", INDEX_OF(REPLACE(REPLACE(root[\"D\"], \"-\", \"\"), \".\", \"\"), \":\"), 10)")
            .And.EndWith(" > \"0000000023:00:000000000\"))");
        Where(x => x.D < m1).Should().StartWith("((root[\"D\"] < \"0\") AND (CONCAT(").And.EndWith(" > \"0000000000:00:010000000\"))");
        Where(x => x.D == h23).Should().Be("(root[\"D\"] = \"23:00:00\")", "the text is canonical, so equality is exact and keeps the index");
    }

    [Fact]
    public void Preparing_twice_changes_nothing()
    {
        var h23 = TimeSpan.FromHours(23);
        var zero = TimeSpan.Zero;
        Expression<Func<Task905TimeSpanLiveTests.Row, bool>> filter = x => !(x.D > h23) && x.D <= x.D2 && (x.DN ?? zero) >= x.D && x.D.Equals(h23);
        var once = CosmosFilterRewrite.Prepare(filter)!;
        var twice = CosmosFilterRewrite.Prepare(once)!;
        Queryable().Where(twice).ToQueryDefinition().QueryText.Should().Be(Queryable().Where(once).ToQueryDefinition().QueryText);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("arithmetic")]
    [InlineData("method")]
    [InlineData("compare")]
    [InlineData("negate")]
    public void A_TimeSpan_shape_with_no_exact_rendering_is_refused_before_any_request(string shape)
    {
        var h = TimeSpan.FromHours(1);
        Expression<Func<Task905TimeSpanLiveTests.Row, bool>> filter = shape switch
        {
            "member" => x => x.D.TotalMinutes > 1,
            "arithmetic" => x => x.D - h > h,
            "method" => x => x.D.Subtract(h) == h,
            "compare" => x => TimeSpan.Compare(x.D, h) == -1,
            _ => x => -x.D < h,
        };
        var act = () => CosmosFilterRewrite.Prepare(filter);
        act.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)");
    }
}

/// <summary>
/// TASK-905: the wall-clock key of a stored DateTime has one producer. The aggregates' SQL rendering
/// (<c>CosmosFilterRewrite.WallClockKeySql</c>, TASK-897's <c>CosmosAggregationHelper.WallClockKey</c>) is the filter's key
/// (<c>CosmosFilterRewrite.WallClockKey</c>, TASK-902) with a <c>.</c> after the second, and both answer the C# key of the
/// value read back.
/// </summary>
public class Task905WallClockKeyTests
{
    private readonly ITestOutputHelper _output;

    public Task905WallClockKeyTests(ITestOutputHelper output) => _output = output;

    private static readonly CosmosClient Offline = new("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");

    private static Expression<Func<Task902DateTimeLiveTests.Row, string>> FilterKey()
    {
        var x = Expression.Parameter(typeof(Task902DateTimeLiveTests.Row), "x");
        return Expression.Lambda<Func<Task902DateTimeLiveTests.Row, string>>(
            CosmosFilterRewrite.WallClockKey(Expression.Property(x, nameof(Task902DateTimeLiveTests.Row.T))), x);
    }

    [Fact]
    public void The_aggregate_key_is_the_filter_key_with_a_point_after_the_second()
    {
        var rendered = Offline.GetContainer("db", "c").GetItemLinqQueryable<Task902DateTimeLiveTests.Row>().Select(FilterKey())
            .ToQueryDefinition().QueryText;
        const string prefix = "SELECT VALUE ";
        const string suffix = " FROM root";
        rendered.Should().StartWith(prefix).And.EndWith(suffix);
        var filterKey = rendered.Substring(prefix.Length, rendered.Length - prefix.Length - suffix.Length);
        const string second = "SUBSTRING(root[\"T\"], 0, 19)";
        filterKey.Should().StartWith("CONCAT(" + second + ", ");

        // What the aggregates send: a DateTime group is grouped on the key without the suffix, and selects the least key with it.
        var (select, aggregateKey) = Aggregation.CosmosAggregationHelper.DateTimeGroupKey("root[\"T\"]");
        aggregateKey.Should().Be("CONCAT(CONCAT(" + second + ", \".\"), " + filterKey.Substring(("CONCAT(" + second + ", ").Length));
        select.Should().StartWith("MIN(IS_NULL(root[\"T\"]) ? undefined : CONCAT(" + aggregateKey + ", RIGHT(root[\"T\"], ");
    }

    [Fact]
    public async Task Both_renderings_answer_the_CSharp_key_of_every_stored_form()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        var settings = new Settings();
        if (string.Equals(Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE"), "Gateway", StringComparison.OrdinalIgnoreCase))
            settings.ConnectionMode = ConnectionMode.Gateway;
        var dbName = "birko_task905k_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Task902DateTimeLiveTests.Row>(conn, dbName, null, settings);
        await store.InitAsync();
        try
        {
            var container = store.Container!;
            foreach (var (row, missing) in Task902DateTimeLiveTests.Seed())
            {
                if (missing.Contains("T")) continue;
                row.Guid = Guid.NewGuid();
                var node = (JsonObject)JsonSerializer.SerializeToNode(row)!;
                node["id"] = row.Guid.ToString();
                await container.UpsertItemAsync(node, new PartitionKey(row.Guid.ToString()!));
            }

            // The aggregates' key (TASK-897) as the store's grouped aggregate groups on it, and with the stored suffix as it
            // selects it; the filter's key (TASK-902) as the LINQ provider renders it.
            var (_, aggregate) = Aggregation.CosmosAggregationHelper.DateTimeGroupKey("c[\"T\"]");
            var withSuffix = CosmosFilterRewrite.WallClockKeySql("c[\"T\"]", withSuffix: true);
            var rows = new List<JsonObject>();
            using (var iterator = container.GetItemQueryIterator<JsonObject>($"SELECT c.Code, c.T, {aggregate} AS a, {withSuffix} AS s FROM c"))
            {
                while (iterator.HasMoreResults) rows.AddRange(await iterator.ReadNextAsync());
            }

            rows.Should().HaveCountGreaterThan(5);
            foreach (var row in rows)
            {
                var code = row["Code"]!.GetValue<string>();
                var stored = row["T"]!.GetValue<string>();
                var readBack = JsonSerializer.Deserialize<DateTime>(JsonSerializer.Serialize(stored));
                var want = CosmosFilterRewrite.WallClockKeyOf(readBack);
                _output.WriteLine($"{stored}: aggregate {row["a"]}, with suffix {row["s"]}, C# {want}");
                row["a"]!.GetValue<string>().Should().Be(want.Insert(19, "."), stored);
                var parsed = JsonSerializer.Deserialize<DateTime>(JsonSerializer.Serialize(row["s"]!.GetValue<string>()));
                parsed.Should().Be(readBack, stored);
                parsed.Kind.Should().Be(readBack.Kind, stored);

                var x = Expression.Parameter(typeof(Task902DateTimeLiveTests.Row), "x");
                var filterKeyIs = Expression.Lambda<Func<Task902DateTimeLiveTests.Row, bool>>(Expression.AndAlso(
                    Expression.Equal(Expression.Property(x, nameof(Task902DateTimeLiveTests.Row.Code)), Expression.Constant(code)),
                    Expression.Equal(CosmosFilterRewrite.WallClockKey(Expression.Property(x, nameof(Task902DateTimeLiveTests.Row.T))), Expression.Constant(want))), x);
                (await container.GetItemLinqQueryable<Task902DateTimeLiveTests.Row>().Where(filterKeyIs).CountAsync()).Resource
                    .Should().Be(1, $"the filter key of {stored} is {want}");
            }
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}
