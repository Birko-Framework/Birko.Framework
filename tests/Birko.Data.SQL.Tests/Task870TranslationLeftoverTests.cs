using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-870 — the parser hands a case fold, a SET <c>.Date</c>, a SET <c>Replace</c> and integer SET arithmetic to the
/// provider's <see cref="SqlValueFragmentDialect"/>, and a provider dialect refuses a decimal division in a filter. The
/// four providers' behaviour is measured live in <c>TranslationLeftoversContract</c>; these pin the hand-off.
/// </summary>
public class Task870TranslationLeftoverTests
{
    [Table("T870Unit")]
    private class Row : AbstractModel
    {
        public string Code { get; set; } = string.Empty;
        public string? Other { get; set; }
        public int Hits { get; set; }
        public decimal Amount { get; set; }
        public DateTime At { get; set; }
    }

    /// <summary>Renders every hook recognisably, so a test sees which one the parser called.</summary>
    private sealed class MarkingDialect : SqlValueFragmentDialect
    {
        public static readonly MarkingDialect Instance = new();

        public override string CaseFold(string operand, bool upper, bool invariant)
            => $"FOLD[{(upper ? "U" : "L")}{(invariant ? "I" : "C")}]({operand})";

        public override string DateOf(string operand) => $"DAY_OF({operand})";

        public override string Replace(string operand, string oldValue, string newValue) => $"REPL({operand}, {oldValue}, {newValue})";

        public override string Arithmetic(ExpressionType op, string sqlOperator, string left, string right, Type type)
            => SqlConversion.IsIntegral(type) ? $"CHECKED({base.Arithmetic(op, sqlOperator, left, right, type)})" : base.Arithmetic(op, sqlOperator, left, right, type);
    }

    private static string? Set<P>(Expression<Func<Row, P>> value, out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>();
        return global::Birko.Data.SQL.DataBase.ParseExpression(value, parameters, false, null, MarkingDialect.Instance);
    }

    private static Condition Single(Expression<Func<Row, bool>> filter) => global::Birko.Data.SQL.DataBase.ParseConditionExpression(filter).Single();

    [Theory]
    [InlineData("ToLower", "L", "C")]
    [InlineData("ToUpper", "U", "C")]
    [InlineData("ToLowerInvariant", "L", "I")]
    [InlineData("ToUpperInvariant", "U", "I")]
    public void A_case_fold_in_a_filter_is_LOWER_or_UPPER_in_the_portable_form_and_the_dialects_fold_on_render(string fold, string upper, string culture)
    {
        Expression<Func<Row, bool>> filter = fold switch
        {
            "ToLower" => x => x.Code.ToLower() == "abc",
            "ToUpper" => x => x.Code.ToUpper() == "abc",
            "ToLowerInvariant" => x => x.Code.ToLowerInvariant() == "abc",
            _ => x => x.Code.ToUpperInvariant() == "abc",
        };

        var condition = Single(filter);

        condition.Name.Should().StartWith(upper == "U" ? "UPPER(" : "LOWER(");
        condition.NameFragment.Should().NotBeNull("the provider re-renders the fold");
        condition.NameFragment!(MarkingDialect.Instance).Should().Be($"FOLD[{upper}{culture}]({condition.Name!.Substring(6, condition.Name.Length - 7)})");
    }

    [Fact]
    public void A_nested_case_fold_re_renders_both_folds()
    {
        var condition = Single(x => x.Code.ToUpper().ToLowerInvariant() == "abc");

        condition.NameFragment!(MarkingDialect.Instance).Should().StartWith("FOLD[LI](FOLD[UC](");
    }

    [Fact]
    public void A_case_fold_inside_a_LIKE_and_an_ordinal_Equals_re_renders_too()
    {
        Single(x => x.Code.ToLower().Contains("ab")).NameFragment!(MarkingDialect.Instance).Should().StartWith("FOLD[LC](");
        Single(x => string.Equals(x.Code.ToUpper(), "AB", StringComparison.Ordinal)).NameFragment!(MarkingDialect.Instance)
            .Should().StartWith("FOLD[UC](");
    }

    [Fact]
    public void A_case_fold_in_a_SET_value_goes_through_the_dialect()
    {
        Set(x => x.Code.ToLower(), out _).Should().StartWith("FOLD[LC](");
        Set(x => x.Code.ToUpperInvariant(), out _).Should().StartWith("FOLD[UI](");
    }

    [Fact]
    public void A_SET_of_a_columns_Date_goes_through_the_dialect_and_is_no_longer_DATE()
    {
        Set(x => x.At.Date, out _).Should().StartWith("DAY_OF(");
        var parameters = new Dictionary<string, object>();
        global::Birko.Data.SQL.DataBase.ParseExpression((Expression<Func<Row, DateTime>>)(x => x.At.Date), parameters, false, null, null)
            .Should().StartWith("CAST(").And.EndWith(" AS DATE)");
    }

    [Fact]
    public void Integer_SET_arithmetic_goes_through_the_dialect_and_decimal_arithmetic_does_not()
    {
        Set(x => x.Hits + 1, out _).Should().StartWith("CHECKED(");
        Set(x => -x.Hits * 2, out _).Should().StartWith("CHECKED(");
        Set(x => x.Amount + 1m, out _).Should().NotContain("CHECKED");
    }

    [Fact]
    public void Replace_in_SET_binds_its_values_and_goes_through_the_dialect()
    {
        var sql = Set(x => x.Code.Replace("b", "X"), out var parameters);

        sql.Should().StartWith("REPL(");
        parameters.Values.Should().Equal("b", "X");
    }

    [Fact]
    public void Replace_in_SET_with_a_null_new_value_removes_as_CSharp_does()
    {
        Set(x => x.Code.Replace("b", null), out var parameters);
        parameters.Values.Should().Equal("b", string.Empty);

        Set(x => x.Code.Replace("b", x.Other), out _).Should().Contain("COALESCE(", "a NULL replacement read from the row removes");
    }

    [Fact]
    public void Replace_in_SET_with_a_char_pair_binds_one_character_strings()
    {
        Set(x => x.Code.Replace('b', 'X'), out var parameters);
        parameters.Values.Should().Equal("b", "X");
    }

    [Fact]
    public void Replace_in_SET_of_an_empty_null_or_row_read_value_is_refused_by_name()
    {
        var empty = string.Empty;
        string? none = null;
        ((Action)(() => Set(x => x.Code.Replace(empty, "X"), out _))).Should().Throw<NotSupportedException>().WithMessage("*Replace*empty*");
        ((Action)(() => Set(x => x.Code.Replace(none!, "X"), out _))).Should().Throw<NotSupportedException>().WithMessage("*Replace*null*");
        ((Action)(() => Set(x => x.Code.Replace(x.Other!, "X"), out _))).Should().Throw<NotSupportedException>().WithMessage("*Replace*read from the row*");
    }

    [Fact]
    public void A_provider_dialect_refuses_a_decimal_division_and_the_portable_form_writes_it()
    {
        var provider = SqlValueFragmentDialect.WithRounding((v, d, m) => $"ROUND({v}, {d})");

        SqlValueFragmentDialect.Portable.Arithmetic(ExpressionType.Divide, "/", "a", "b", typeof(decimal)).Should().Be("(a / b)");
        ((Action)(() => provider.Arithmetic(ExpressionType.Divide, "/", "a", "b", typeof(decimal?))))
            .Should().Throw<NotSupportedException>().WithMessage("*division of a decimal*");
        provider.Arithmetic(ExpressionType.Modulo, "%", "a", "b", typeof(decimal)).Should().Be("(a % b)", "a decimal modulo is exact");
        provider.Arithmetic(ExpressionType.Divide, "/", "a", "b", typeof(double)).Should().Be("(a / b)");
        provider.Arithmetic(ExpressionType.Divide, "/", "a", "b", typeof(int)).Should().Be("(a / b)");
    }

    [Theory]
    [InlineData("4", "0.25")]
    [InlineData("2", "0.5")]
    [InlineData("2.5", "0.4")]
    [InlineData("-0.5", "-2")]
    [InlineData("100", "0.01")]
    [InlineData("0.0016", "625")]
    [InlineData("1024", "0.0009765625")]
    [InlineData("1", "1")]
    public void A_divisor_whose_reciprocal_terminates_has_an_exact_reciprocal(string divisor, string reciprocal)
    {
        var d = decimal.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture);
        global::Birko.Data.SQL.DataBase.TerminatingReciprocal(d, out var r).Should().BeTrue();
        r.Should().Be(decimal.Parse(reciprocal, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0.3")]
    [InlineData("7")]
    [InlineData("300007")]
    [InlineData("0")]
    [InlineData("1099511627776")] // 2^40: the reciprocal has 28 significant digits, past the bound
    public void A_divisor_whose_reciprocal_does_not_terminate_or_is_too_long_has_none(string divisor)
        => global::Birko.Data.SQL.DataBase.TerminatingReciprocal(
            decimal.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture), out _).Should().BeFalse();

    [Fact]
    public void A_decimal_division_by_a_terminating_constant_is_an_exact_product_in_a_filter_and_in_SET()
    {
        var provider = SqlValueFragmentDialect.WithRounding((v, d, m) => $"ROUND({v}, {d})");
        var condition = Single(x => x.Amount / 4m > 1m);

        condition.NameFragment!(provider).Should().Contain("*").And.NotContain("/");
        Set(x => x.Amount / 2.5m, out var parameters).Should().Contain("*").And.NotContain("/");
        parameters.Values.Should().Equal(0.4m);
    }

    [Fact]
    public void A_decimal_division_by_3m_or_a_column_in_SET_is_the_dialects_to_refuse()
    {
        var provider = SqlValueFragmentDialect.WithRounding((v, d, m) => $"ROUND({v}, {d})");
        var parameters = new Dictionary<string, object>();
        ((Action)(() => global::Birko.Data.SQL.DataBase.ParseExpression((Expression<Func<Row, decimal>>)(x => x.Amount / 3m), parameters, false, null, provider)))
            .Should().Throw<NotSupportedException>().WithMessage("*division of a decimal*");
        ((Action)(() => global::Birko.Data.SQL.DataBase.ParseExpression((Expression<Func<Row, decimal>>)(x => x.Amount / x.Amount), parameters, false, null, provider)))
            .Should().Throw<NotSupportedException>().WithMessage("*division of a decimal*");
    }

    [Fact]
    public void A_decimal_division_in_a_filter_is_refused_by_the_providers_re_render()
    {
        var condition = Single(x => x.Amount / 3m > 1m);
        var provider = SqlValueFragmentDialect.WithRounding((v, d, m) => $"ROUND({v}, {d})");

        condition.NameFragment.Should().NotBeNull();
        ((Action)(() => condition.NameFragment!(provider))).Should().Throw<NotSupportedException>().WithMessage("*division of a decimal*");
    }
}
