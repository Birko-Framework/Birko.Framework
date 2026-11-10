using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// TASK-906, live RavenDB: an aggregate view's MIN, MAX and AVG skip nulls, as <see cref="AggregateHelper.LinqAggregate{T}"/>
/// does over the same rows. Measured before: the index's MIN of {3, null} was null and its AVG 1.5 (C#: 3 and 3). Gated on
/// <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task906RavenViewNullAggregateLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task906RavenViewNullAggregateLiveTests(ITestOutputHelper output) => _output = output;

    public class T906Row : AbstractModel
    {
        public string? Grp { get; set; }
        public int? Score { get; set; }
        public decimal? Price { get; set; }
        public double? Ratio { get; set; }
    }

    public class Totals
    {
        public string? Grp { get; set; }
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
        public int? MaxScore { get; set; }
        public double? AvgScore { get; set; }
        public decimal? SumPrice { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal? AvgPrice { get; set; }
        public double? MinRatio { get; set; }
        public double? MaxRatio { get; set; }
        public double? AvgRatio { get; set; }
    }

    private static ViewDefinitionBuilder<Totals> Metrics(ViewDefinitionBuilder<Totals> b) => b
        .Count<T906Row>(v => v.Cnt)
        .Sum<T906Row, int?>(r => r.Score, v => v.SumScore)
        .Min<T906Row, int?>(r => r.Score, v => v.MinScore)
        .Max<T906Row, int?>(r => r.Score, v => v.MaxScore)
        .Avg<T906Row, double?>(r => r.Score, v => v.AvgScore)
        .Sum<T906Row, decimal?>(r => r.Price, v => v.SumPrice)
        .Min<T906Row, decimal?>(r => r.Price, v => v.MinPrice)
        .Max<T906Row, decimal?>(r => r.Price, v => v.MaxPrice)
        .Avg<T906Row, decimal?>(r => r.Price, v => v.AvgPrice)
        .Min<T906Row, double?>(r => r.Ratio, v => v.MinRatio)
        .Max<T906Row, double?>(r => r.Ratio, v => v.MaxRatio)
        .Avg<T906Row, double?>(r => r.Ratio, v => v.AvgRatio);

    private static ViewDefinition Grouped() => Metrics(new ViewDefinitionBuilder<Totals>()
            .HasName("t906_grouped").HasQueryMode(ViewQueryMode.Persistent)
            .From<T906Row>()
            .GroupBy<T906Row, string?>(r => r.Grp)
            .Select<T906Row, string?>(r => r.Grp, v => v.Grp))
        .Build();

    private static ViewDefinition Whole() => Metrics(new ViewDefinitionBuilder<Totals>()
            .HasName("t906_whole").HasQueryMode(ViewQueryMode.Persistent)
            .From<T906Row>())
        .Build();

    private static readonly AggregateField[] Oracle =
    {
        new(AggregateFunction.Count, "Grp", "Cnt"),
        new(AggregateFunction.Sum, "Score", "SumScore"),
        new(AggregateFunction.Min, "Score", "MinScore"),
        new(AggregateFunction.Max, "Score", "MaxScore"),
        new(AggregateFunction.Avg, "Score", "AvgScore"),
        new(AggregateFunction.Sum, "Price", "SumPrice"),
        new(AggregateFunction.Min, "Price", "MinPrice"),
        new(AggregateFunction.Max, "Price", "MaxPrice"),
        new(AggregateFunction.Avg, "Price", "AvgPrice"),
        new(AggregateFunction.Min, "Ratio", "MinRatio"),
        new(AggregateFunction.Max, "Ratio", "MaxRatio"),
        new(AggregateFunction.Avg, "Ratio", "AvgRatio"),
    };

    /// <summary>Groups with a null beside a value, only nulls, only values, and a negative value beside a null.</summary>
    private static IEnumerable<T906Row> Seed()
    {
        yield return new T906Row { Grp = "a", Score = 3, Price = 1.5m, Ratio = 0.5 };
        yield return new T906Row { Grp = "a", Score = null, Price = null, Ratio = null };
        yield return new T906Row { Grp = "b", Score = null, Price = null, Ratio = null };
        yield return new T906Row { Grp = "b", Score = null, Price = null, Ratio = null };
        yield return new T906Row { Grp = "c", Score = 4, Price = 2m, Ratio = 1.5 };
        yield return new T906Row { Grp = "c", Score = 6, Price = 3m, Ratio = 2.5 };
        yield return new T906Row { Grp = "d", Score = null, Price = null, Ratio = null };
        yield return new T906Row { Grp = "d", Score = -2, Price = -4m, Ratio = -1 };
        yield return new T906Row { Grp = "d", Score = null, Price = null, Ratio = null };
    }

    private async Task Run(Func<IDocumentStore, AsyncRavenDBStore<T906Row>, Task> body)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        using var rows = new AsyncRavenDBStore<T906Row>(Url!, "birko-task906-" + Guid.NewGuid().ToString("N"));
        try
        {
            await rows.InitAsync();
            await body(rows.DocumentStore!, rows);
        }
        finally
        {
            try { await rows.DestroyAsync(); } catch { }
        }
    }

    private async Task Ensure(IDocumentStore store, ViewDefinition definition)
    {
        await new RavenViewManager(store).EnsureAsync(definition);
        for (var i = 0; i < 100 && (await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!))).IsStale; i++)
            await Task.Delay(100);
        foreach (var errors in await store.Maintenance.SendAsync(new GetIndexErrorsOperation(new[] { definition.Name! })))
            foreach (var error in errors.Errors.Take(3))
                _output.WriteLine($"index error ({error.Action}): {error.Error}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Min_max_and_avg_skip_nulls_as_LINQ_does(bool grouped) => Run(async (store, rows) =>
    {
        foreach (var row in Seed()) await rows.CreateAsync(row);
        var definition = grouped ? Grouped() : Whole();
        await Ensure(store, definition);

        var got = (await new RavenViewStore<Totals>(store, definition).QueryAsync()).Select(v => Render(v, grouped)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var oracle = AggregateHelper.LinqAggregate(Seed().ToList(), new AggregateQuery<T906Row>
        {
            GroupByFields = grouped ? new[] { "Grp" } : Array.Empty<string>(),
            Aggregates = Oracle,
        });
        var expected = oracle.Select(r => Render(r.Values)).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var because = $"grouped={grouped}\n expected [{string.Join(" | ", expected)}]\n got      [{string.Join(" | ", got)}]";
        _output.WriteLine(because);
        got.Should().Equal(expected, because);
    });

    private static string Render(Totals view, bool grouped)
    {
        var values = typeof(Totals).GetProperties()
            .Where(p => grouped || p.Name != nameof(Totals.Grp))
            .ToDictionary(p => p.Name, p => p.GetValue(view));
        return Render(values);
    }

    /// <summary>A row as text, keys in ordinal order; every number as its decimal value, so the view's widths do not matter.</summary>
    private static string Render(IReadOnlyDictionary<string, object?> row)
        => string.Join("; ", row.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value switch
        {
            null => "null",
            string s => s,
            double d when double.IsNaN(d) => "NaN",
            IConvertible c => Convert.ToDecimal(c, CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture),
            var other => other.ToString(),
        }));
}
