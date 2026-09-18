using System.Globalization;
using System.Threading;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// CR-L196: ViewSelectSqlBuilder.FormatJoinConditionValue emits numerics/bools unquoted (numerics via
    /// InvariantCulture so a comma-decimal locale can't corrupt the SQL) and single-quotes strings with
    /// embedded quotes doubled.
    /// </summary>
    public class ViewJoinConditionValueTests
    {
        [Fact]
        public void String_value_is_single_quoted_and_escaped()
        {
            ViewSelectSqlBuilder.FormatJoinConditionValue("a'b").Should().Be("'a''b'");
        }

        [Fact]
        public void Integer_value_is_unquoted()
        {
            ViewSelectSqlBuilder.FormatJoinConditionValue(42).Should().Be("42");
        }

        [Fact]
        public void Bool_values_map_to_TRUE_FALSE_unquoted()
        {
            ViewSelectSqlBuilder.FormatJoinConditionValue(true).Should().Be("TRUE");
            ViewSelectSqlBuilder.FormatJoinConditionValue(false).Should().Be("FALSE");
        }

        [Fact]
        public void Decimal_value_uses_invariant_culture_even_under_comma_locale()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // A locale whose decimal separator is ',' must NOT produce "1,5" in the SQL literal.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                ViewSelectSqlBuilder.FormatJoinConditionValue(1.5m).Should().Be("1.5");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
