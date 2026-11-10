using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Tests.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Aggregation;

public enum T906Level { Low, Mid, High }

public class T906Doc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int? Score { get; set; }
    public decimal? Price { get; set; }
    public DateTime When { get; set; }
    public DateTime? Seen { get; set; }
    public Guid? Owner { get; set; }
    public T906Level Level { get; set; }
    public bool Flag { get; set; }
}

/// <summary>
/// TASK-906, live MongoDB: the store aggregate answers what <see cref="AggregateHelper.LinqAggregate{T}"/> answers over the
/// same rows (as a read returns them): ordering, offset, limit, time buckets, and the types of a non-numeric Min / Max and
/// of a group key. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public sealed class Task906MongoStoreAggregateLiveTests : IDisposable
{
    private readonly MongoLive _live;
    private readonly ITestOutputHelper _output;

    public Task906MongoStoreAggregateLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new MongoLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static readonly Guid OwnerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IEnumerable<T906Doc> Seed()
    {
        var day = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        yield return new T906Doc { Kind = "a", Name = "pear", Score = 3, Price = 1.5m, When = day.AddMinutes(10), Seen = day, Owner = OwnerA, Level = T906Level.Mid, Flag = true };
        yield return new T906Doc { Kind = "a", Name = "apple", Score = null, Price = null, When = day.AddMinutes(70), Seen = null, Owner = null, Level = T906Level.Low, Flag = false };
        yield return new T906Doc { Kind = "b", Name = "fig", Score = 7, Price = 2.25m, When = day.AddMinutes(75), Seen = day.AddDays(1), Owner = OwnerB, Level = T906Level.High, Flag = true };
        yield return new T906Doc { Kind = "b", Name = null, Score = 1, Price = 4m, When = day.AddMinutes(200), Seen = day.AddDays(-1), Owner = OwnerA, Level = T906Level.Mid, Flag = false };
        yield return new T906Doc { Kind = "c", Name = "kiwi", Score = 9, Price = 0.5m, When = day.AddMinutes(-30), Seen = day.AddHours(5), Owner = OwnerB, Level = T906Level.Low, Flag = true };
        yield return new T906Doc { Kind = null, Name = "lime", Score = 2, Price = null, When = day.AddMinutes(130), Seen = null, Owner = null, Level = T906Level.High, Flag = false };
    }

    private static readonly AggregateField[] Totals =
    {
        new(AggregateFunction.Count, "Kind", "count"),
        new(AggregateFunction.Sum, "Score", "sumScore"),
        new(AggregateFunction.Max, "Price", "maxPrice"),
    };

    private static readonly AggregateField[] NonNumeric =
    {
        new(AggregateFunction.Min, "Name", "minName"),
        new(AggregateFunction.Max, "Name", "maxName"),
        new(AggregateFunction.Min, "When", "minWhen"),
        new(AggregateFunction.Max, "Seen", "maxSeen"),
        new(AggregateFunction.Min, "Owner", "minOwner"),
        new(AggregateFunction.Max, "Level", "maxLevel"),
        new(AggregateFunction.Min, "Flag", "minFlag"),
    };

    public static IEnumerable<object[]> Cases => from name in Queries.Keys from mode in new[] { "sync", "async" } select new object[] { name, mode };

    private static readonly IReadOnlyDictionary<string, AggregateQuery<T906Doc>> Queries = new Dictionary<string, AggregateQuery<T906Doc>>
    {
        ["ordered by a sum, descending"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = Totals, OrderBy = new Dictionary<string, bool> { ["sumScore"] = true } },
        ["ordered by a key, ascending"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = Totals, OrderBy = new Dictionary<string, bool> { ["Kind"] = false } },
        ["offset and limit"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = Totals, OrderBy = new Dictionary<string, bool> { ["Kind"] = false }, Offset = 1, Limit = 2 },
        ["limit only"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = Totals, OrderBy = new Dictionary<string, bool> { ["count"] = true, ["Kind"] = false }, Limit = 1 },
        ["ungrouped, offset past the row"] = new() { Aggregates = Totals, Offset = 1 },
        ["hourly buckets"] = new() { Aggregates = Totals, TimeColumn = "When", TimeBucketInterval = "1 hour" },
        ["hourly buckets by kind"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = Totals, TimeColumn = "When", TimeBucketInterval = "01:00:00" },
        ["daily buckets of a nullable time"] = new() { Aggregates = Totals, TimeColumn = "Seen", TimeBucketInterval = "1 day" },
        ["non-numeric min and max"] = new() { Aggregates = NonNumeric },
        ["non-numeric min and max by kind"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = NonNumeric },
        ["grouped by a Guid, an enum and a bool"] = new() { GroupByFields = new[] { "Owner", "Level", "Flag" }, Aggregates = Totals },
        ["grouped by a time"] = new() { GroupByFields = new[] { "Seen" }, Aggregates = Totals },
        ["grouped by the identity, names in another case"] = new()
        {
            GroupByFields = new[] { "guid", "KIND" },
            Aggregates = new AggregateField[] { new(AggregateFunction.Sum, "score", "s"), new(AggregateFunction.Max, "level", "l") },
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_store_aggregate_answers_as_LINQ_over_the_same_rows(string name, string mode)
    {
        if (!_live.Ready()) return;
        var query = Queries[name];

        IReadOnlyList<AggregateResult> got;
        IEnumerable<T906Doc> rows;
        Exception? thrown = null;
        got = Array.Empty<AggregateResult>();
        if (mode == "async")
        {
            var store = new AsyncMongoDBStore<T906Doc>();
            store.SetSettings(_live.NewSettings());
            foreach (var doc in Seed()) await store.CreateAsync(doc);
            rows = (await store.ReadAsync(CancellationToken.None)).ToList();
            try { got = await store.AggregateAsync(query); } catch (Exception ex) { thrown = ex; }
        }
        else
        {
            var store = new MongoDBStore<T906Doc>();
            store.SetSettings(_live.NewSettings());
            foreach (var doc in Seed()) store.Create(doc);
            rows = store.Read().ToList();
            try { got = store.Aggregate(query); } catch (Exception ex) { thrown = ex; }
        }

        // Without an ordering the order of the groups is the store's, so only the set is compared.
        var expected = Render(AggregateHelper.LinqAggregate(rows, query));
        var actual = Render(got);
        if (query.OrderBy == null)
        {
            expected.Sort(StringComparer.Ordinal);
            actual.Sort(StringComparer.Ordinal);
        }
        var because = $"{mode} '{name}':\n expected [{string.Join(" | ", expected)}]\n got      [{string.Join(" | ", actual)}]\n thrown {thrown?.GetType().Name}: {thrown?.Message}";
        _output.WriteLine(because);
        thrown.Should().BeNull(because);
        actual.Should().Equal(expected, because);
    }

    public static IEnumerable<object[]> Refusals => from name in Refused.Keys from mode in new[] { "sync", "async" } select new object[] { name, mode };

    private static readonly IReadOnlyDictionary<string, (AggregateQuery<T906Doc> Query, Type Exception)> Refused = new Dictionary<string, (AggregateQuery<T906Doc>, Type)>
    {
        ["a weekly bucket (does not divide 0001-01-01 to 1970-01-01)"] = (new() { Aggregates = Totals, TimeColumn = "When", TimeBucketInterval = "7.00:00:00" }, typeof(NotSupportedException)),
        ["a bucket finer than a millisecond"] = (new() { Aggregates = Totals, TimeColumn = "When", TimeBucketInterval = "00:00:00.0005" }, typeof(NotSupportedException)),
        ["a bucket of a number"] = (new() { Aggregates = Totals, TimeColumn = "Score", TimeBucketInterval = "1 hour" }, typeof(NotSupportedException)),
        ["a bucket without a time column"] = (new() { Aggregates = Totals, TimeBucketInterval = "1 hour" }, typeof(ArgumentException)),
        ["a group-by field that is no property"] = (new() { GroupByFields = new[] { "Nope" }, Aggregates = Totals }, typeof(ArgumentException)),
        ["an aggregate source that is no property"] = (new() { Aggregates = new AggregateField[] { new(AggregateFunction.Max, "Nope", "m") } }, typeof(ArgumentException)),
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_query_MongoDB_cannot_answer_as_LINQ_is_refused_by_name(string name, string mode)
    {
        if (!_live.Ready()) return;
        var (query, expected) = Refused[name];

        Exception? thrown = null;
        IReadOnlyList<AggregateResult> got = Array.Empty<AggregateResult>();
        try
        {
            if (mode == "async")
            {
                var store = new AsyncMongoDBStore<T906Doc>();
                store.SetSettings(_live.NewSettings());
                foreach (var doc in Seed()) await store.CreateAsync(doc);
                got = await store.AggregateAsync(query);
            }
            else
            {
                var store = new MongoDBStore<T906Doc>();
                store.SetSettings(_live.NewSettings());
                foreach (var doc in Seed()) store.Create(doc);
                got = store.Aggregate(query);
            }
        }
        catch (Exception ex) { thrown = ex; }

        var because = $"{mode} '{name}' (got [{string.Join(" | ", Render(got))}]; thrown {thrown?.GetType().Name}: {thrown?.Message})";
        _output.WriteLine(because);
        thrown.Should().NotBeNull(because);
        thrown!.GetType().Should().Be(expected, because);
    }

    /// <summary>Each row as text, in the answer's order: every key with its value's CLR type, keys in ordinal order.</summary>
    internal static List<string> Render(IEnumerable<AggregateResult> rows)
        => rows.Select(r => string.Join("; ", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={RenderValue(kv.Value)}"))).ToList();

    private static string RenderValue(object? value) => value switch
    {
        null => "null",
        decimal d => "Decimal:" + d.ToString("0.############################", CultureInfo.InvariantCulture),
        DateTime t => "DateTime:" + t.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => value.GetType().Name + ":" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value,
    };
}
