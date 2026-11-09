using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-873 — <see cref="SqlValueFragmentDialect.OrdinalTextEquality"/>, the one producer of an exact string equality, and the
/// parser's marking of every string <c>==</c> / <c>!=</c> / <c>IN</c> it builds.
/// </summary>
public class Task873OrdinalTextEqualityTests
{
    private sealed class Wrapping : SqlValueFragmentDialect
    {
        protected override string? OrdinalText(string operand, bool bothFromRow) => bothFromRow ? $"WIDE({operand})" : $"BIN({operand})";

        protected override IEnumerable<string> OrdinalValue(string parameter, object? value)
        {
            yield return $"V({parameter})";
            if (value is string { Length: >= 4 }) yield return $"LONG({parameter})";
        }
    }

    private static readonly SqlValueFragmentDialect Base = SqlValueFragmentDialect.WithRounding((v, d, m) => v);

    [Fact]
    public void An_equality_keeps_the_plain_comparison_in_front_for_the_index_and_adds_the_exact_one()
        => new Wrapping().OrdinalTextEquality("Code", new[] { "@p" }, new object?[] { "a" }, isNot: false)
            .Should().Be("(Code = @p AND BIN(Code) = V(@p))");

    [Fact]
    public void A_list_keeps_the_plain_IN_in_front_and_adds_the_exact_one()
        => new Wrapping().OrdinalTextEquality("Code", new[] { "@a", "@b" }, new object?[] { "a", "b" }, isNot: false)
            .Should().Be("(Code IN (@a, @b) AND BIN(Code) IN (V(@a), V(@b)))");

    [Fact]
    public void A_value_with_two_exact_forms_becomes_a_list_of_both()
        => new Wrapping().OrdinalTextEquality("Code", new[] { "@p" }, new object?[] { "long" }, isNot: false)
            .Should().Be("(Code = @p AND BIN(Code) IN (V(@p), LONG(@p)))");

    [Fact]
    public void A_negation_negates_the_whole_test_so_it_holds_wherever_the_plain_term_fails()
    {
        var dialect = new Wrapping();
        dialect.OrdinalTextEquality("Code", new[] { "@p" }, new object?[] { "a" }, isNot: true).Should().Be("NOT (Code = @p AND BIN(Code) = V(@p))");
        dialect.OrdinalTextEquality("Code", new[] { "@a", "@b" }, new object?[] { "a", "b" }, isNot: true)
            .Should().Be("NOT (Code IN (@a, @b) AND BIN(Code) IN (V(@a), V(@b)))");
    }

    [Fact]
    public void Two_operands_from_the_row_take_the_row_form_on_both_sides()
        => new Wrapping().OrdinalTextEquality("A", new[] { "B" }, null, isNot: false)
            .Should().Be("(A = B AND WIDE(A) = WIDE(B))");

    [Fact]
    public void A_dialect_without_an_exact_form_renders_the_plain_comparison()
    {
        Base.OrdinalTextEquality("Code", new[] { "@p" }, new object?[] { "a" }, false).Should().Be("Code = @p");
        Base.OrdinalTextEquality("Code", new[] { "@a", "@b" }, new object?[] { "a", "b" }, true).Should().Be("Code NOT IN (@a, @b)");
    }

    [Fact]
    public void An_equality_without_a_value_or_with_a_value_count_that_does_not_match_is_refused()
    {
        var empty = () => Base.OrdinalTextEquality("Code", Array.Empty<string>(), Array.Empty<object?>(), false);
        empty.Should().Throw<ArgumentException>();
        var mismatched = () => Base.OrdinalTextEquality("Code", new[] { "@a", "@b" }, new object?[] { "a" }, false);
        mismatched.Should().Throw<ArgumentException>();
    }

    [Birko.Data.SQL.Attributes.Table("T873Rows")]
    public class Row : Birko.Data.Models.AbstractModel
    {
        public string? Text { get; set; }
        public string? Other { get; set; }
        public int Number { get; set; }
    }

    private static Condition Leaf(Expression<Func<Row, bool>> filter)
    {
        var condition = System.Linq.Enumerable.Single(Birko.Data.SQL.DataBase.ParseConditionExpression(filter));
        while (condition.SubConditions != null && System.Linq.Enumerable.Any(condition.SubConditions))
            condition = System.Linq.Enumerable.First(condition.SubConditions);
        return condition;
    }

    private static readonly string[] List = { "a", "b" };

    [Fact]
    public void The_parser_marks_every_string_equality_and_list_as_ordinal()
    {
        Leaf(x => x.Text == "a").Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => x.Text != "a").Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => "a" == x.Text).Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => x.Text!.Equals("a")).Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => List.Contains(x.Text)).Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => x.Text == x.Other).Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => x.Text!.ToLower() == "a").Comparison.Should().Be(StringComparison.Ordinal);
        Leaf(x => (x.Text ?? "") == "a").Comparison.Should().Be(StringComparison.Ordinal);
    }

    [Fact]
    public void The_parser_leaves_a_number_and_a_null_test_alone()
    {
        Leaf(x => x.Number == 3).Comparison.Should().BeNull();
        Leaf(x => x.Text == null).Comparison.Should().BeNull();
    }
}
