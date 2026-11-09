using System;
using System.Data;
using System.Reflection;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// TASK-887 — an aggregate is read into its view property by one rule: a numeric property gets the server's value
    /// converted exactly, whatever type the server answered in; COUNT / SUM / AVG that cannot answer a number, or SUM / AVG
    /// over a column that is not one, are refused when the field is created. The live four-provider contract is
    /// <c>Task887GroupedViewReadTests</c> in Birko.Data.SQL.Views.Tests.
    /// </summary>
    public class Task887FunctionFieldTests
    {
        private sealed class Source
        {
            public int Hits { get; set; }
            public long Big { get; set; }
            public double Ratio { get; set; }
            public string? Name { get; set; }
        }

        private sealed class Target
        {
            public int Int { get; set; }
            public int? NullableInt { get; set; }
            public long Long { get; set; }
            public short Short { get; set; }
            public double Double { get; set; }
            public decimal Decimal { get; set; }
            public string? Text { get; set; }
            public DayOfWeek Day { get; set; }
        }

        private static PropertyInfo S(string name) => typeof(Source).GetProperty(name)!;
        private static PropertyInfo T(string name) => typeof(Target).GetProperty(name)!;

        private static Target Read(FunctionField field, object? raw, Type column)
        {
            var table = new DataTable();
            table.Columns.Add("v", column);
            table.Rows.Add(raw ?? DBNull.Value);
            using var reader = table.CreateDataReader();
            reader.Read();
            var target = new Target();
            field.Read(target, reader, 0);
            return target;
        }

        [Theory]
        [InlineData(typeof(decimal), "3000000010", 3000000010L)] // SUM(BIGINT) on PostgreSQL and MySQL
        [InlineData(typeof(long), "7", 7L)]
        [InlineData(typeof(double), "12", 12L)]
        [InlineData(typeof(string), "42.000", 42L)]              // SQLite keeps a decimal column as text
        public void A_whole_number_in_any_server_type_reads_into_a_long(Type column, string raw, long expected)
        {
            var field = FunctionField.CreateFunctionField(T(nameof(Target.Long)), "SUM", new LongField(S(nameof(Source.Big)), "Big"));
            var value = column == typeof(string) ? (object)raw : Convert.ChangeType(raw, column, System.Globalization.CultureInfo.InvariantCulture);

            Read(field, value, column).Long.Should().Be(expected);
        }

        [Fact]
        public void A_fraction_into_an_integral_property_is_refused_by_name()
        {
            var field = FunctionField.CreateFunctionField(T(nameof(Target.Int)), "AVG", new IntegerField(S(nameof(Source.Hits)), "Hits"));

            var act = () => Read(field, 2.5m, typeof(decimal));

            act.Should().Throw<InvalidCastException>().WithMessage("*AVG(Hits * 1.0E0) answered 2.5*not a whole number*Target.Int*");
        }

        [Fact]
        public void A_value_out_of_range_is_an_overflow_naming_the_property()
        {
            var field = FunctionField.CreateFunctionField(T(nameof(Target.Short)), "SUM", new ShortField(typeof(Target).GetProperty(nameof(Target.Short))!, "Small"));

            var act = () => Read(field, 70000L, typeof(long));

            act.Should().Throw<OverflowException>().WithMessage("*70000*Target.Short*");
        }

        [Fact]
        public void Null_reads_as_null_into_a_nullable_property_and_is_refused_into_a_non_nullable_one()
        {
            var nullable = FunctionField.CreateFunctionField(T(nameof(Target.NullableInt)), "SUM", new NullableIntegerField(S(nameof(Source.Hits)), "Hits"));
            Read(nullable, null, typeof(long)).NullableInt.Should().BeNull();

            var plain = FunctionField.CreateFunctionField(T(nameof(Target.Int)), "SUM", new IntegerField(S(nameof(Source.Hits)), "Hits"));
            var act = () => Read(plain, null, typeof(long));
            act.Should().Throw<InvalidCastException>().WithMessage("*answered NULL*Target.Int*");
        }

        [Fact]
        public void An_average_reads_into_a_double_and_an_enum_reads_by_its_underlying_value()
        {
            var avg = FunctionField.CreateFunctionField(T(nameof(Target.Double)), "AVG", new DoubleField(S(nameof(Source.Ratio)), "Ratio"));
            Read(avg, 1.25m, typeof(decimal)).Double.Should().Be(1.25);

            var max = FunctionField.CreateFunctionField(T(nameof(Target.Day)), "MAX", new IntegerField(S(nameof(Source.Hits)), "Hits"));
            Read(max, 3L, typeof(long)).Day.Should().Be(DayOfWeek.Wednesday);
        }

        [Fact]
        public void A_long_or_double_source_is_no_longer_read_as_text()
        {
            FunctionField.CreateFunctionField(T(nameof(Target.Long)), "MAX", new LongField(S(nameof(Source.Big)), "Big"))
                .Should().NotBeOfType<StringFunction>().And.Match<FunctionField>(f => f.Type == DbType.Int64);
            FunctionField.CreateFunctionField(T(nameof(Target.Double)), "SUM", new DoubleField(S(nameof(Source.Ratio)), "Ratio"))
                .Should().NotBeOfType<StringFunction>().And.Match<FunctionField>(f => f.Type == DbType.Double);
        }

        [Fact]
        public void An_average_over_an_integral_column_is_rendered_fractional()
        {
            FunctionField.CreateFunctionField(T(nameof(Target.Double)), "AVG", new IntegerField(S(nameof(Source.Hits)), "Hits"))
                .Parameters.Should().Equal("Hits * 1.0E0");
            FunctionField.CreateFunctionField(T(nameof(Target.Double)), "AVG", new DoubleField(S(nameof(Source.Ratio)), "Ratio"))
                .Parameters.Should().Equal("Ratio");
        }

        [Fact]
        public void A_non_numeric_min_reads_through_the_source_columns_reader()
        {
            var field = FunctionField.CreateFunctionField(T(nameof(Target.Text)), "MIN", new StringField(S(nameof(Source.Name)), "Name"));

            Read(field, "k", typeof(string)).Text.Should().Be("k");
            Read(field, null, typeof(string)).Text.Should().BeNull();
        }

        [Fact]
        public void Sum_over_a_text_column_is_refused_when_the_field_is_created()
        {
            var act = () => FunctionField.CreateFunctionField(T(nameof(Target.Long)), "SUM", new StringField(S(nameof(Source.Name)), "Name"));

            act.Should().Throw<NotSupportedException>().WithMessage("*SUM(Name)*numeric column*");
        }

        [Theory]
        [InlineData("COUNT")]
        [InlineData("SUM")]
        [InlineData("AVG")]
        public void A_numeric_aggregate_into_a_text_property_is_refused_when_the_field_is_created(string function)
        {
            var act = () => FunctionField.CreateFunctionField(T(nameof(Target.Text)), function, new IntegerField(S(nameof(Source.Hits)), "Hits"));

            act.Should().Throw<NotSupportedException>().WithMessage($"*Target.Text*{function} answers a number*");
        }
    }
}
