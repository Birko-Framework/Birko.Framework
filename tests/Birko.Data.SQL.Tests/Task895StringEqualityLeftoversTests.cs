using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-895 — the condition shapes behind the string-equality leftovers of TASK-873. The rows each shape selects on every
/// provider are checked by the shared <c>StringEqualityLeftoversContract</c>; these pin the parse and the plan.
/// </summary>
public class Task895StringEqualityLeftoversTests
{
    [Table("T895Shapes")]
    public class Row : AbstractModel
    {
        public string? Text { get; set; }
        public string? Other { get; set; }
        public int Number { get; set; }
        public Guid Ref { get; set; }
    }

    private static Condition Leaf(Expression<Func<Row, bool>> filter)
    {
        var condition = Birko.Data.SQL.DataBase.ParseConditionExpression(filter).Single();
        while (condition.SubConditions != null && condition.SubConditions.Any())
            condition = condition.SubConditions.First();
        return condition;
    }

    private static readonly StringComparison Ordinal = StringComparison.Ordinal;

    // ── (c) / (d): string.Equals(…, Ordinal) is ==

    [Fact]
    public void String_Equals_with_Ordinal_parses_as_the_exact_equality_that_equals_does()
    {
        foreach (var filter in new Expression<Func<Row, bool>>[]
                 {
                     x => string.Equals(x.Text, "a", StringComparison.Ordinal),
                     x => x.Text!.Equals("a", StringComparison.Ordinal),
                     x => string.Equals("a", x.Text, StringComparison.Ordinal),
                     x => string.Equals(x.Text, "a", Ordinal),
                 })
        {
            var leaf = Leaf(filter);
            leaf.Type.Should().Be(ConditionType.Equal, filter.ToString());
            leaf.Comparison.Should().Be(StringComparison.Ordinal);
            leaf.Values!.Cast<object>().Should().Equal("a");
        }
    }

    [Fact]
    public void String_Equals_with_Ordinal_over_two_columns_is_a_column_equality_instead_of_a_refusal()
    {
        var leaf = Leaf(x => string.Equals(x.Text, x.Other, StringComparison.Ordinal));

        leaf.Type.Should().Be(ConditionType.Equal);
        leaf.IsField.Should().BeTrue();
        leaf.Comparison.Should().Be(StringComparison.Ordinal);
    }

    [Fact]
    public void String_Equals_with_Ordinal_against_null_is_the_null_test()
    {
        var leaf = Leaf(x => string.Equals(x.Text, null, StringComparison.Ordinal));

        leaf.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void OrdinalIgnoreCase_keeps_its_exact_LIKE_and_stays_refused_over_two_columns()
    {
        var leaf = Leaf(x => string.Equals(x.Text, "a", StringComparison.OrdinalIgnoreCase));
        leaf.Type.Should().Be(ConditionType.TextEqual);
        leaf.Comparison.Should().Be(StringComparison.OrdinalIgnoreCase);

        var twoColumns = () => Leaf(x => string.Equals(x.Text, x.Other, StringComparison.OrdinalIgnoreCase));
        twoColumns.Should().Throw<NotSupportedException>().WithMessage("*OrdinalIgnoreCase*");
    }

    [Fact]
    public void A_culture_comparison_is_still_refused_by_name()
    {
        var act = () => Leaf(x => string.Equals(x.Text, "a", StringComparison.CurrentCulture));
        act.Should().Throw<NotSupportedException>().WithMessage("*CurrentCulture*");
    }

    // ── (b): a rule's string equality is marked exact, by the producer the lambda path uses

    private static Condition RuleLeaf(IRule rule, bool typed = true)
    {
        var condition = (typed ? RuleConditionConverter.ToConditions<Row>(rule) : RuleConditionConverter.ToConditions(rule)).Single();
        while (condition.SubConditions != null && condition.SubConditions.Any())
            condition = condition.SubConditions.First();
        return condition;
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal)]
    [InlineData(ComparisonOperator.NotEqual)]
    [InlineData(ComparisonOperator.In)]
    [InlineData(ComparisonOperator.NotIn)]
    public void A_rule_string_equality_is_marked_ordinal(ComparisonOperator op)
    {
        var value = op is ComparisonOperator.In or ComparisonOperator.NotIn ? new object[] { "a", "b" } : (object)"a";

        RuleLeaf(new Rule("Text", op, value)).Comparison.Should().Be(StringComparison.Ordinal, "typed");
        RuleLeaf(new Rule("Text", op, value), typed: false).Comparison.Should().Be(StringComparison.Ordinal, "type-less");
    }

    [Fact]
    public void A_rule_that_is_not_a_string_equality_keeps_the_collation_or_has_none()
    {
        RuleLeaf(new Rule("Number", ComparisonOperator.Equal, 5)).Comparison.Should().BeNull("a number");
        RuleLeaf(new Rule("Ref", ComparisonOperator.Equal, Guid.NewGuid().ToString())).Comparison.Should().BeNull(
            "text against a Guid column is converted there, not compared as text");
        RuleLeaf(new Rule("Text", ComparisonOperator.Like, "abc")).Comparison.Should().BeNull(
            "Like without a wildcard follows the collation, as every LIKE-family rule does");
        RuleLeaf(new Rule("Text", ComparisonOperator.Equal, null)).Type.Should().Be(ConditionType.IsNull);
    }

    // ── (a): the planner splits an exact string list

    private static Condition In(IEnumerable<object?> values, StringComparison? comparison)
        => new("W.Name", values.ToArray(), ConditionType.In) { Comparison = comparison };

    private static InListChunkPlan Plan(Condition leaf)
        => InListChunkPlanner.Plan("read", new[] { leaf }, 40, 10, conditions => conditions.Sum(c => c.Values!.Cast<object?>().Count()));

    [Fact]
    public void An_exact_string_list_past_the_maximum_is_split_into_disjoint_chunks_with_its_duplicates_dropped()
    {
        var values = Enumerable.Range(0, 45).Select(i => (object?)("n" + i)).Concat(new object?[] { "n0", "N0", "n0 " }).ToList();

        var plan = Plan(In(values, StringComparison.Ordinal));

        plan.IsChunked.Should().BeTrue();
        var chunked = plan.Chunks!.SelectMany(c => c.Single().Values!.Cast<object?>()).ToList();
        chunked.Should().HaveCount(47, "45 names, N0 and 'n0 ' are distinct C# strings; the second n0 goes")
            .And.OnlyHaveUniqueItems();
        plan.Chunks!.Should().OnlyContain(c => c.Single().Comparison == StringComparison.Ordinal, "each chunk stays exact");
    }

    [Fact]
    public void A_string_list_built_by_hand_and_a_char_list_are_still_refused_past_the_maximum()
    {
        var strings = Plan(In(Enumerable.Range(0, 45).Select(i => (object?)("n" + i)), comparison: null));
        strings.IsRefused.Should().BeTrue();
        strings.Refusal.Should().Contain("collation");

        var chars = Plan(In(Enumerable.Range(0, 45).Select(i => (object?)(char)('a' + i)), StringComparison.Ordinal));
        chars.IsRefused.Should().BeTrue();
    }
}
