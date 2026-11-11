using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Tests.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Aggregation;

/// <summary>
/// TASK-909 — a collection created with a default collation (<c>db.createCollection(name, { collation: … })</c>, which Birko
/// never does but a consumer's DBA may) does not change the store's <c>Aggregate</c>: the groups, the <c>$min</c> /
/// <c>$max</c> of a string and the filter answer what <see cref="AggregateHelper"/> answers over the same rows, in Ordinal
/// order with exact string equality (TASK-873, TASK-907). An aggregation inherits the collection's default collation
/// unless it names one (MongoDB manual), so the store's aggregation names the simple collation.
/// <para>
/// Measured before (live, MongoDB 7): see the task file. Every collation strength changed the answer: the <c>$min</c> /
/// <c>$max</c> followed the locale (<c>apple</c> below <c>Banana</c>), and at strength 1 or 2 the <c>$group</c> merged
/// <c>zebra</c> with <c>Zebra</c> and the <c>$match</c> of <c>Kind == "zebra"</c> matched <c>Zebra</c>.
/// </para>
/// </summary>
public sealed class Task909MongoCollationLiveTests : IDisposable
{
    private readonly MongoLive _live;
    private readonly ITestOutputHelper _output;

    public Task909MongoCollationLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new MongoLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static readonly (string Kind, string? Name, int N)[] Seed =
    {
        ("zebra", "b", 1), ("Zebra", "a", 2),
        ("case", "apple", 3), ("case", "Banana", 4),
        ("accent", "zebra", 5), ("accent", "éclair", 6),
        ("eszett", "ss", 7), ("eszett", "ß", 8),
        ("space", "a", 9), ("space", "a ", 10),
    };

    public static IEnumerable<object[]> Collations
        => from strength in new[] { "primary", "secondary", "tertiary" }
           from mode in new[] { "sync", "async" }
           select new object[] { strength, mode };

    private static string Show(object? value)
        => value switch
        {
            null => "null",
            string s => "\"" + string.Concat(s.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

    private static string Render(IEnumerable<AggregateResult> rows)
        => string.Join(" | ", rows.Select(r => string.Join(", ", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={Show(kv.Value)}"))));

    private static IEnumerable<(string Name, AggregateQuery<StringOrderDoc> Query)> Queries()
    {
        var metrics = new[]
        {
            new AggregateField(AggregateFunction.Count, "N", "count"),
            new AggregateField(AggregateFunction.Min, "Name", "min"),
            new AggregateField(AggregateFunction.Max, "Name", "max"),
        };
        yield return ("grouped by Kind, ordered by Kind", new AggregateQuery<StringOrderDoc>
        {
            GroupByFields = new[] { "Kind" }, Aggregates = metrics, OrderBy = new Dictionary<string, bool> { ["Kind"] = false },
        });
        yield return ("ungrouped", new AggregateQuery<StringOrderDoc> { Aggregates = metrics });
        yield return ("filtered Kind == \"zebra\"", new AggregateQuery<StringOrderDoc> { Filter = x => x.Kind == "zebra", Aggregates = metrics });
    }

    [Theory]
    [MemberData(nameof(Collations))]
    public async Task A_collection_default_collation_does_not_change_the_aggregate(string strength, string mode)
    {
        if (!_live.Ready()) return;
        var settings = _live.NewSettings();
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        var collation = new Collation("en", strength: strength switch
        {
            "primary" => CollationStrength.Primary,
            "secondary" => CollationStrength.Secondary,
            _ => CollationStrength.Tertiary,
        });
        new MongoClient($"mongodb://{host}:27017").GetDatabase(settings.Name)
            .CreateCollection(nameof(StringOrderDoc), new CreateCollectionOptions { Collation = collation });

        var rows = Seed.Select(s => new StringOrderDoc { Kind = s.Kind, Name = s.Name, N = s.N }).ToList();
        IAggregatableStore<StringOrderDoc>? sync = null;
        IAsyncAggregatableStore<StringOrderDoc>? async = null;
        if (mode == "async")
        {
            var store = new AsyncMongoDBStore<StringOrderDoc>();
            store.SetSettings(settings);
            foreach (var row in rows) await store.CreateAsync(row);
            async = store;
        }
        else
        {
            var store = new MongoDBStore<StringOrderDoc>();
            store.SetSettings(settings);
            foreach (var row in rows) store.Create(row);
            sync = store;
        }

        var failures = new List<string>();
        foreach (var (name, query) in Queries())
        {
            var expected = Render(AggregateHelper.LinqAggregate(rows, query));
            string actual;
            try { actual = Render(async != null ? await async.AggregateAsync(query) : sync!.Aggregate(query)); }
            catch (Exception ex) { actual = $"{ex.GetType().Name}: {ex.Message}"; }
            _output.WriteLine($"MEASURE | en/{strength} | {mode} | {name} | {actual}{(actual == expected ? "" : " | expected " + expected)}");
            if (actual != expected) failures.Add($"{name}: got [{actual}], expected [{expected}]");
        }

        failures.Should().BeEmpty();
    }
}
