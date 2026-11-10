using System;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Fields;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// TASK-892 — one producer renders an aggregate for the store's <c>Aggregate</c> and for a view
    /// (<see cref="SqlValueFragmentDialect.Aggregate"/>), and one rule reads it back (<see cref="AggregateValue.Convert"/>).
    /// The provider forms (SQLite's exact decimal aggregates, PostgreSQL's double average) and the answers are measured
    /// live by <c>Task892AggregateTests</c> in Birko.Data.SQL.Views.Tests.
    /// </summary>
    public class Task892AggregateRenderingTests
    {
        private static readonly SqlValueFragmentDialect Provider = SqlValueFragmentDialect.WithRounding((v, d, m) => $"ROUND({v}, {d})");

        [Fact]
        public void A_view_sum_answers_zero_over_no_values_and_the_store_sum_answers_null()
        {
            Provider.Aggregate(AggregateFunction.Sum, "T.Hits", typeof(int?), typeof(int?), sumOfNoValuesIsZero: true)
                .Should().Be("COALESCE(SUM(T.Hits), 0)");
            Provider.Aggregate(AggregateFunction.Sum, "T.Hits", typeof(int?), typeof(int), sumOfNoValuesIsZero: false)
                .Should().Be("SUM(T.Hits)");
        }

        [Fact]
        public void An_integral_average_is_a_double_average()
        {
            Provider.Aggregate(AggregateFunction.Avg, "Hits", typeof(int), typeof(double), false).Should().Be("AVG(Hits * 1.0E0)");
            Provider.Aggregate(AggregateFunction.Avg, "Ratio", typeof(double), typeof(double), false).Should().Be("AVG(Ratio)");
        }

        [Theory]
        [InlineData(typeof(decimal), typeof(decimal))]
        [InlineData(typeof(int), typeof(decimal))]
        [InlineData(typeof(decimal), typeof(double))]
        public void A_provider_refuses_an_average_it_cannot_compute_as_CSharp_does(Type source, Type result)
        {
            Provider.HasExactAverage(source, result).Should().BeFalse();

            var act = () => Provider.Aggregate(AggregateFunction.Avg, "Price", source, result, false);

            act.Should().Throw<NotSupportedException>().WithMessage("*AVG(Price)*28 to 29 significant digits*TASK-892*");
        }

        [Fact]
        public void The_portable_form_never_refuses()
        {
            SqlValueFragmentDialect.Portable.Aggregate(AggregateFunction.Avg, "Price", typeof(decimal), typeof(decimal), false)
                .Should().Be("AVG(Price)");
        }

        [Fact]
        public void Count_and_min_max_render_as_written()
        {
            Provider.Aggregate(AggregateFunction.Count, null, null, typeof(int), false).Should().Be("COUNT(*)");
            Provider.Aggregate(AggregateFunction.Count, "T.Guid", typeof(Guid), typeof(long), true).Should().Be("COUNT(T.Guid)");
            Provider.Aggregate(AggregateFunction.Min, "Name", typeof(string), typeof(string), true).Should().Be("MIN(Name)");
        }

        [Theory]
        [InlineData(3L, typeof(int), 3)]
        [InlineData("2.50", typeof(decimal), 2.5)]
        public void The_reading_rule_converts_exactly(object raw, Type declared, double expected)
        {
            Convert.ToDouble(AggregateValue.Convert(raw, declared, "SUM(x)", "aggregate 'v'")).Should().Be(expected);
        }

        [Fact]
        public void The_reading_rule_names_the_store_aggregate_it_refuses()
        {
            var act = () => AggregateValue.Convert(5_000_000_000L, typeof(int), "COUNT(*)", "aggregate 'n'");

            act.Should().Throw<OverflowException>().WithMessage("*COUNT(*)*aggregate 'n'*Int32*");
        }
    }
}
