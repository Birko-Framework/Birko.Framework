using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-906, live MongoDB: an aggregate view's SUM, AVG, MIN and MAX of a <c>decimal</c>, <c>double</c> and <c>long</c>
/// member, read into view properties of those types, answer what <see cref="AggregateHelper.LinqAggregate{T}"/> answers over
/// the same rows: over no rows (the ungrouped empty row, whose Sum is an <c>Int32</c> 0), over only nulls (<c>$sum</c>
/// answers an <c>Int32</c> 0), and over values. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task906MongoViewSumTypesLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task906MongoViewSumTypesLiveTests(ITestOutputHelper output) => _output = output;

    public class T906Row : AbstractModel
    {
        public string? Grp { get; set; }
        public decimal? Price { get; set; }
        public decimal Amount { get; set; }
        public double? Ratio { get; set; }
        public long? Big { get; set; }
    }

    public class Totals
    {
        public string? Grp { get; set; }
        public long Cnt { get; set; }
        public decimal? SumPrice { get; set; }
        public decimal? AvgPrice { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal SumAmount { get; set; }
        public double? SumRatio { get; set; }
        public long? SumBig { get; set; }
    }

    private static ViewDefinitionBuilder<Totals> Metrics(ViewDefinitionBuilder<Totals> b) => b
        .Count<T906Row>(v => v.Cnt)
        .Sum<T906Row, decimal?>(r => r.Price, v => v.SumPrice)
        .Avg<T906Row, decimal?>(r => r.Price, v => v.AvgPrice)
        .Min<T906Row, decimal?>(r => r.Price, v => v.MinPrice)
        .Max<T906Row, decimal?>(r => r.Price, v => v.MaxPrice)
        .Sum<T906Row, decimal>(r => r.Amount, v => v.SumAmount)
        .Sum<T906Row, double?>(r => r.Ratio, v => v.SumRatio)
        .Sum<T906Row, long?>(r => r.Big, v => v.SumBig);

    private static ViewDefinition Whole(ViewQueryMode mode) => Metrics(new ViewDefinitionBuilder<Totals>()
            .HasName("t906_whole").HasQueryMode(mode).From<T906Row>())
        .Build();

    private static ViewDefinition Grouped(ViewQueryMode mode) => Metrics(new ViewDefinitionBuilder<Totals>()
            .HasName("t906_grouped").HasQueryMode(mode).From<T906Row>()
            .GroupBy<T906Row, string?>(r => r.Grp)
            .Select<T906Row, string?>(r => r.Grp, v => v.Grp))
        .Build();

    private static readonly (AggregateFunction Function, string Source, string View)[] Oracle =
    {
        (AggregateFunction.Count, "Grp", "Cnt"),
        (AggregateFunction.Sum, "Price", "SumPrice"),
        (AggregateFunction.Avg, "Price", "AvgPrice"),
        (AggregateFunction.Min, "Price", "MinPrice"),
        (AggregateFunction.Max, "Price", "MaxPrice"),
        (AggregateFunction.Sum, "Amount", "SumAmount"),
        (AggregateFunction.Sum, "Ratio", "SumRatio"),
        (AggregateFunction.Sum, "Big", "SumBig"),
    };

    public static IEnumerable<object[]> Cases
        => from seed in new[] { "no rows", "only nulls", "values" }
           from grouped in new[] { false, true }
           from mode in new[] { ViewQueryMode.OnTheFly, ViewQueryMode.Persistent }
           select new object[] { seed, grouped, mode };

    private static IEnumerable<T906Row> Seed(string seed) => seed switch
    {
        "no rows" => Array.Empty<T906Row>(),
        "only nulls" => new[]
        {
            new T906Row { Grp = "a" },
            new T906Row { Grp = "b" },
            new T906Row { Grp = "b" },
        },
        _ => new[]
        {
            new T906Row { Grp = "a", Price = 0.1m, Amount = 1.25m, Ratio = 0.5, Big = 5_000_000_000 },
            new T906Row { Grp = "a", Price = 0.2m, Amount = 2.5m, Ratio = null, Big = null },
            new T906Row { Grp = "b", Price = null, Amount = 0m, Ratio = 1.5, Big = 1 },
            new T906Row { Grp = "b", Price = 3.333m, Amount = -1m, Ratio = 2, Big = 2 },
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_view_reads_each_aggregate_into_its_property_as_LINQ_answers(string seed, bool grouped, ViewQueryMode mode)
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
            return;
        }
        var settings = new Settings(host, "birko_task906_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            var store = new AsyncMongoDBStore<T906Row>();
            store.SetSettings(settings);
            await store.InitAsync();
            foreach (var row in Seed(seed)) await store.CreateAsync(row);
            var rows = (await store.ReadAsync(CancellationToken.None)).ToList();

            var definition = grouped ? Grouped(mode) : Whole(mode);
            if (mode == ViewQueryMode.Persistent) await new MongoViewManager(client).EnsureAsync(definition);

            Exception? thrown = null;
            var got = new List<string>();
            try
            {
                got = (await new MongoViewStore<Totals>(client, definition).QueryAsync()).Select(v => Render(v, grouped)).ToList();
            }
            catch (Exception ex) { thrown = ex; }
            got.Sort(StringComparer.Ordinal);

            var oracle = AggregateHelper.LinqAggregate(rows, new AggregateQuery<T906Row>
            {
                GroupByFields = grouped ? new[] { "Grp" } : Array.Empty<string>(),
                Aggregates = Oracle.Select(o => new AggregateField(o.Function, o.Source, o.View)).ToArray(),
            });
            var expected = oracle.Select(r => Render(AsView(r), grouped)).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var because = $"{seed}, grouped={grouped}, {mode}\n expected [{string.Join(" | ", expected)}]\n got      [{string.Join(" | ", got)}]\n thrown {thrown?.GetType().Name}: {thrown?.Message}";
            _output.WriteLine(because);
            thrown.Should().BeNull(because);
            got.Should().Equal(expected, because);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    /// <summary>The oracle's row as the view type, each value converted to its property's type (an empty value stays the default).</summary>
    private static Totals AsView(AggregateResult result)
    {
        var view = new Totals();
        foreach (var (key, value) in result.Values)
        {
            var property = typeof(Totals).GetProperty(key)!;
            if (value == null) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            property.SetValue(view, Convert.ChangeType(value, type, CultureInfo.InvariantCulture));
        }
        return view;
    }

    private static string Render(Totals view, bool grouped)
        => string.Join("; ", typeof(Totals).GetProperties()
            .Where(p => grouped || p.Name != nameof(Totals.Grp))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name + "=" + p.GetValue(view) switch
            {
                null => "null",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString(),
            }));
}
