using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores;

/// <summary>
/// TASK-907 — the in-memory aggregation (the oracle every provider is measured against) takes a string Min / Max in the
/// query's <see cref="AggregateQuery{T}.StringComparison"/>, <see cref="StringComparison.Ordinal"/> by default (the user's
/// decision, 2026-11-10). Measured before: <see cref="Comparer{T}.Default"/>, the current culture's order, so the answer
/// depended on the machine (sk-SK: <c>"apple"</c> below <c>"Banana"</c>).
/// </summary>
public class Task907AggregateStringOrderTests
{
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int N { get; set; }
    }

    private static readonly string?[] Names = { "apple", "Banana", "éclair", "zebra", "Zebra", "a ", "a", "ß", "ss", "\U0001F600", "～", null };

    private static IReadOnlyList<Row> Rows() => Names.Select((n, i) => new Row { Name = n, N = i }).ToList();

    private static AggregateResult Extremes(StringComparison? comparison)
    {
        var aggregates = new[] { new AggregateField(AggregateFunction.Min, "Name", "min"), new AggregateField(AggregateFunction.Max, "Name", "max") };
        var query = comparison == null
            ? new AggregateQuery<Row> { Aggregates = aggregates }
            : new AggregateQuery<Row> { Aggregates = aggregates, StringComparison = comparison.Value };
        return AggregateHelper.LinqAggregate(Rows(), query).Single();
    }

    private static void WithCulture(string name, Action body)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try { body(); }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_query_orders_strings_ordinally_by_default()
        => new AggregateQuery<Row>().StringComparison.Should().Be(StringComparison.Ordinal);

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void The_default_string_min_and_max_are_Ordinal_in_every_culture(string culture) => WithCulture(culture, () =>
    {
        var row = Extremes(null);
        row.GetValue<string>("min").Should().Be("Banana");
        // U+FF5E is above U+1F600 in UTF-16 (D83D DE00), which is C#'s Ordinal; above "éclair" too.
        row.GetValue<string>("max").Should().Be("～");
    });

    public static IEnumerable<object[]> Comparisons
        => Enum.GetValues<StringComparison>().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void Every_comparison_is_honoured_as_its_StringComparer_orders(StringComparison comparison) => WithCulture("sk-SK", () =>
    {
        var comparer = StringComparer.FromComparison(comparison);
        var values = Names.Where(n => n != null).Cast<string>().ToList();
        var row = Extremes(comparison);

        comparer.Compare(row.GetValue<string>("min"), values.Min(comparer)).Should().Be(0);
        comparer.Compare(row.GetValue<string>("max"), values.Max(comparer)).Should().Be(0);
        values.Should().OnlyContain(v => comparer.Compare(row.GetValue<string>("min"), v) <= 0 && comparer.Compare(row.GetValue<string>("max"), v) >= 0);
    });

    [Fact]
    public void The_comparison_orders_only_strings()
    {
        var row = AggregateHelper.LinqAggregate(Rows(), new AggregateQuery<Row>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Min, "N", "min"), new AggregateField(AggregateFunction.Max, "N", "max") },
            StringComparison = StringComparison.CurrentCultureIgnoreCase,
        }).Single();

        row.GetValue<int>("min").Should().Be(0);
        row.GetValue<int>("max").Should().Be(Names.Length - 1);
    }

    [Fact]
    public void A_filtered_copy_keeps_the_comparison()
    {
        var query = new AggregateQuery<Row> { Aggregates = new[] { new AggregateField(AggregateFunction.Min, "Name", "m") }, StringComparison = StringComparison.InvariantCulture };
        query.WithFilter(r => r.N > 1).StringComparison.Should().Be(StringComparison.InvariantCulture);
    }

    [Fact]
    public void ComputeAggregate_defaults_to_Ordinal()
        => WithCulture("sk-SK", () =>
            AggregateMath.ComputeAggregate(AggregateFunction.Min, new List<object?> { "apple", "Banana", null }, typeof(string), 3)
                .Should().Be("Banana"));

    [Fact]
    public void The_refusal_names_the_comparison_the_member_and_the_order_taken()
    {
        var refusal = AggregateMath.StringOrderNotSupported("X aggregate of Row", AggregateFunction.Max, "Row.Name", StringComparison.CurrentCulture);
        refusal.Message.Should().Contain("Max").And.Contain("Row.Name").And.Contain("CurrentCulture").And.Contain("Ordinal").And.Contain("TASK-907");

        AggregateMath.StringOrderNotSupported("X", AggregateFunction.Min, "Row.Name", StringComparison.Ordinal, null)
            .Message.Should().Contain("no string Min / Max in an exact order");
    }
}
