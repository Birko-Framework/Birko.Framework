using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

[Table("T911BucketRows")]
public class T911BucketRow : AbstractModel
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public int N { get; set; }
    public DateTime Ts { get; set; }
}

/// <summary>
/// TASK-911 — grouped by a string key and a time bucket together (the one SQL provider with <c>time_bucket</c>), the groups
/// are exactly the oracle's: twins under some collation (case, a trailing space, <c>ß</c> / <c>ss</c>, the empty string)
/// keep their own group in each bucket, with the bucket as a group key alongside them, sync and async. Rows of its
/// own table (<see cref="T911BucketRow"/>, so it never races the contract's), whose <c>Ts</c> is the time column.
/// </summary>
public sealed class Task911TimeBucketStringGroupLiveTests
{
    private readonly TimescaleDBLive _live;
    public Task911TimeBucketStringGroupLiveTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);

    private static readonly string?[] Keys = { "Zebra", "zebra", "a", "a ", "ß", "ss", "", " ", null };
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static List<T911BucketRow> Rows()
    {
        var rows = new List<T911BucketRow>();
        for (var i = 0; i < Keys.Length; i++)
        {
            for (var j = 0; j < 4; j++)
                rows.Add(new T911BucketRow { Kind = Keys[i], Name = j % 2 == 0 ? "x" : "X", N = i * 10 + j, Ts = Start.AddMinutes(25 * j + i) });
        }
        return rows;
    }

    private static AggregateQuery<T911BucketRow> Query() => new()
    {
        GroupByFields = new[] { "Kind" },
        TimeBucketInterval = "1 hour",
        TimeColumn = "Ts",
        Aggregates = new[]
        {
            new AggregateField(AggregateFunction.Count, "N", "count"),
            new AggregateField(AggregateFunction.Sum, "N", "sum"),
            new AggregateField(AggregateFunction.Min, "Name", "min"),
        },
    };

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => "\"" + string.Concat(s.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString())) + "\"",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => value.GetType().Name + ":" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Render(AggregateResult r)
        => $"{Show(r.GetValue<object>("Kind"))} @ {Show(r.GetValue<object>("bucket_time"))}: {Show(r.GetValue<object>("count"))} "
           + $"{Show(r.GetValue<object>("sum"))} {Show(r.GetValue<object>("min"))}";

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    public async Task A_string_key_and_a_time_bucket_make_exactly_the_groups_CSharp_makes(string mode)
    {
        if (!_live.Ready()) return;
        var rows = Rows();
        var expected = AggregateHelper.LinqAggregate(rows, Query()).Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();

        IReadOnlyList<AggregateResult> results;
        if (mode == "async")
        {
            var store = new AsyncTimescaleDBStore<T911BucketRow>();
            store.SetSettings(_live.Settings());
            await store.DestroyAsync();
            foreach (var row in rows) await store.CreateAsync(row);
            results = await store.AggregateAsync(Query());
        }
        else
        {
            var store = new TimescaleDBStore<T911BucketRow>();
            store.SetSettings(_live.Settings());
            store.Destroy();
            foreach (var row in rows) store.Create(row);
            results = store.Aggregate(Query());
        }
        var got = results.Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();

        got.Should().Equal(expected, $"{got.Count} groups where C# makes {expected.Count}; missing [{string.Join(" | ", expected.Except(got))}]; "
                                     + $"extra [{string.Join(" | ", got.Except(expected))}]");
    }
}
