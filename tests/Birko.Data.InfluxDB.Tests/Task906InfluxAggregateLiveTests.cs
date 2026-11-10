using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.InfluxDB.Tests;

public class T906Point : AbstractModel
{
    public string? Kind { get; set; }
    public int? Score { get; set; }
    public double? Ratio { get; set; }
    public decimal? Price { get; set; }
    public DateTime When { get; set; }
}

/// <summary>
/// TASK-906, live InfluxDB: the store aggregate answers what <see cref="AggregateHelper.LinqAggregate{T}"/> answers over the
/// rows a read returns, on the query shape the native Flux path took (one aggregate, no filter). Gated on
/// <c>BIRKO_INFLUX_HOST</c>; logs in as <c>BIRKO_INFLUX_USER</c> / <c>BIRKO_INFLUX_PASSWORD</c> (the CI fixture's
/// <c>birko</c> / <c>birkopassword</c> by default) to create a bucket and a token scoped to it.
/// </summary>
public sealed class Task906InfluxAggregateLiveTests
{
    private const string Org = "birko";
    private readonly ITestOutputHelper _output;
    public Task906InfluxAggregateLiveTests(ITestOutputHelper output) => _output = output;

    private static readonly DateTime Day = new(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<T906Point> Seed(string seed) => seed switch
    {
        "no rows" => Array.Empty<T906Point>(),
        "only nulls" => new[]
        {
            new T906Point { Kind = "a", When = Day },
            new T906Point { Kind = "b", When = Day.AddMinutes(90) },
        },
        _ => new[]
        {
            new T906Point { Kind = "a", Score = 3, Ratio = 0.5, Price = 0.1m, When = Day.AddMinutes(10) },
            new T906Point { Kind = "a", Score = null, Ratio = null, Price = null, When = Day.AddMinutes(70) },
            new T906Point { Kind = "b", Score = 7, Ratio = 1.25, Price = 0.2m, When = Day.AddMinutes(75) },
            new T906Point { Kind = "b", Score = -1, Ratio = null, Price = 3m, When = Day.AddMinutes(200) },
            new T906Point { Kind = null, Score = 2, Ratio = 4, Price = null, When = Day.AddMinutes(130) },
        },
    };

    private static readonly IReadOnlyDictionary<string, AggregateQuery<T906Point>> Queries = new Dictionary<string, AggregateQuery<T906Point>>
    {
        ["count"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Count, "Kind", "n") } },
        ["sum of an int"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Sum, "Score", "s") } },
        ["sum of a decimal"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Sum, "Price", "s") } },
        ["avg of an int"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Avg, "Score", "a") } },
        ["min of a double"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Min, "Ratio", "m") } },
        ["max of an int"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Max, "Score", "m") } },
        ["sum by kind"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = new AggregateField[] { new(AggregateFunction.Sum, "Score", "s") } },
        ["count by kind, ordered, limited"] = new()
        {
            GroupByFields = new[] { "Kind" }, Aggregates = new AggregateField[] { new(AggregateFunction.Count, "Kind", "n") },
            OrderBy = new Dictionary<string, bool> { ["n"] = true, ["Kind"] = false }, Limit = 2,
        },
        ["hourly sum of the time column"] = new() { Aggregates = new AggregateField[] { new(AggregateFunction.Sum, "Score", "s") }, TimeColumn = "When", TimeBucketInterval = "1 hour" },
    };

    public static IEnumerable<object[]> Cases
        => from seed in new[] { "no rows", "only nulls", "values" } from name in Queries.Keys select new object[] { seed, name };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_store_aggregate_answers_as_LINQ_over_the_same_rows(string seed, string name)
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_INFLUX_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live InfluxDB. Set BIRKO_INFLUX_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_INFLUX_PORT"), out var p) ? p : 8086;
        var url = $"http://{host}:{port}";
        var user = Environment.GetEnvironmentVariable("BIRKO_INFLUX_USER") ?? "birko";
        var password = Environment.GetEnvironmentVariable("BIRKO_INFLUX_PASSWORD") ?? "birkopassword";

        using var admin = new global::InfluxDB.Client.InfluxDBClient(new InfluxDBClientOptions(url) { Username = user, Password = password });
        var org = (await admin.GetOrganizationsApi().FindOrganizationsAsync(org: Org)).First();
        var bucket = await admin.GetBucketsApi().CreateBucketAsync("birko_task906_" + Guid.NewGuid().ToString("N"), org.Id);
        var resource = new PermissionResource(type: PermissionResource.TypeBuckets, id: bucket.Id, orgID: org.Id);
        var token = await admin.GetAuthorizationsApi().CreateAuthorizationAsync(org.Id, new List<Permission>
        {
            new(Permission.ActionEnum.Read, resource),
            new(Permission.ActionEnum.Write, resource),
        });
        try
        {
            using var store = new AsyncInfluxDBStore<T906Point>();
            store.SetSettings(new Settings(url, bucket.Name, token.Token, Org));
            foreach (var row in Seed(seed)) await store.CreateAsync(row);
            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            var query = Queries[name];

            Exception? thrown = null;
            IReadOnlyList<AggregateResult> got = Array.Empty<AggregateResult>();
            try { got = await store.AggregateAsync(query); } catch (Exception ex) { thrown = ex; }

            var expected = Render(AggregateHelper.LinqAggregate(rows, query), query.OrderBy != null);
            var actual = Render(got, query.OrderBy != null);
            var because = $"{seed}, '{name}'\n expected [{string.Join(" | ", expected)}]\n got      [{string.Join(" | ", actual)}]\n thrown {thrown?.GetType().Name}: {thrown?.InnerException?.Message ?? thrown?.Message}";
            _output.WriteLine(because);
            thrown.Should().BeNull(because);
            actual.Should().Equal(expected, because);
        }
        finally
        {
            try { await admin.GetAuthorizationsApi().DeleteAuthorizationAsync(token); } catch { }
            try { await admin.GetBucketsApi().DeleteBucketAsync(bucket); } catch { }
        }
    }

    /// <summary>Each row as text, keys in ordinal order with each value's type; in the answer's order when it is ordered.</summary>
    private static List<string> Render(IEnumerable<AggregateResult> rows, bool ordered)
    {
        var rendered = rows.Select(r => string.Join("; ", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value switch
        {
            null => "null",
            DateTime t => "DateTime:" + t.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => kv.Value.GetType().Name + ":" + f.ToString(null, CultureInfo.InvariantCulture),
            var other => other.GetType().Name + ":" + other,
        }))).ToList();
        if (!ordered) rendered.Sort(StringComparer.Ordinal);
        return rendered;
    }
}
