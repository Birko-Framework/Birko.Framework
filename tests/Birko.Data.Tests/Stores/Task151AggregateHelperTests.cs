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
/// TASK-151: the LINQ aggregation fallback (in-memory stores and any provider without native aggregation).
/// SH-M377 group key labels, SH-M383 raw rows paging, SH-M384 key collisions, SH-M385 small integral sums,
/// SH-M386 invariant interval parsing.
/// </summary>
public class Task151AggregateHelperTests
{
    public class Row : AbstractModel
    {
        public string? A { get; set; }
        public string? B { get; set; }
        public short Qty { get; set; }
        public byte Small { get; set; }
        public ulong Big { get; set; }
        public DateTime At { get; set; }
    }

    private static AggregateResult Single(IEnumerable<Row> rows, AggregateQuery<Row> query) => AggregateHelper.LinqAggregate(rows, query).Single();

    [Fact]
    public void An_unknown_group_by_field_is_refused_rather_than_shifting_the_labels()
    {
        var rows = new[] { new Row { A = "x", B = "y" } };
        FluentActions.Invoking(() => AggregateHelper.LinqAggregate(rows, new AggregateQuery<Row> { GroupByFields = new[] { "Bogus", "B" } }))
            .Should().Throw<ArgumentException>().WithMessage("*Bogus*");
        FluentActions.Invoking(() => AggregateHelper.LinqAggregate(rows, new AggregateQuery<Row>
            {
                Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Nope") }
            }))
            .Should().Throw<ArgumentException>().WithMessage("*Nope*");
    }

    [Fact]
    public void Raw_rows_are_ordered_and_paged()
    {
        var rows = Enumerable.Range(1, 30).Select(i => new Row { Qty = (short)i }).ToList();

        var page = AggregateHelper.LinqAggregate(rows, new AggregateQuery<Row>
        {
            OrderBy = new Dictionary<string, bool> { ["Qty"] = true },
            Offset = 5,
            Limit = 3,
        });

        page.Select(r => r.GetValue<short>("Qty")).Should().Equal((short)25, (short)24, (short)23);
    }

    [Fact]
    public void Key_tuples_that_join_to_the_same_text_stay_apart()
    {
        var rows = new[]
        {
            new Row { A = "a|b", B = "c", Qty = 1 },
            new Row { A = "a", B = "b|c", Qty = 2 },
            new Row { A = null, B = "z", Qty = 4 },
            new Row { A = "", B = "z", Qty = 8 },
        };

        var groups = AggregateHelper.LinqAggregate(rows, new AggregateQuery<Row>
        {
            GroupByFields = new[] { "A", "B" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Qty", "total") },
        });

        groups.Should().HaveCount(4);
        groups.Select(g => g.GetValue<long>("total")).Should().BeEquivalentTo(new long[] { 1, 2, 4, 8 });
    }

    [Fact]
    public void Small_and_unsigned_integral_columns_sum_and_average()
    {
        var rows = new[] { new Row { Qty = 10, Small = 1, Big = ulong.MaxValue }, new Row { Qty = 15, Small = 2, Big = 1 } };

        var result = Single(rows, new AggregateQuery<Row>
        {
            Aggregates = new[]
            {
                new AggregateField(AggregateFunction.Sum, "Qty", "sumQty"),
                new AggregateField(AggregateFunction.Avg, "Qty", "avgQty"),
                new AggregateField(AggregateFunction.Sum, "Small", "sumSmall"),
                new AggregateField(AggregateFunction.Sum, "Big", "sumBig"),
            }
        });

        Convert.ToInt64(result.Values["sumQty"]).Should().Be(25);
        Convert.ToDouble(result.Values["avgQty"]).Should().Be(12.5);
        Convert.ToInt64(result.Values["sumSmall"]).Should().Be(3);
        Convert.ToDecimal(result.Values["sumBig"]).Should().Be((decimal)ulong.MaxValue + 1);
        FluentActions.Invoking(() => Single(rows, new AggregateQuery<Row> { Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "A") } }))
            .Should().Throw<NotSupportedException>("a sum of strings is refused, not answered null");
    }

    [Fact]
    public void An_interval_parses_the_same_in_every_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sk-SK");
            TimeIntervalParser.Parse("1.5 hours").Should().Be(TimeSpan.FromMinutes(90));
            TimeIntervalParser.Parse("00:15:00").Should().Be(TimeSpan.FromMinutes(15));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void An_unparseable_interval_is_refused_rather_than_disabling_bucketing()
    {
        var rows = new[] { new Row { At = DateTime.UtcNow } };
        FluentActions.Invoking(() => AggregateHelper.LinqAggregate(rows, new AggregateQuery<Row>
            {
                Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Qty") },
                TimeBucketInterval = "1 fortnight",
                TimeColumn = "At",
            }))
            .Should().Throw<ArgumentException>();
    }
}
