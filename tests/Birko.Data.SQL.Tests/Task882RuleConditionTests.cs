using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-882 — the condition shapes <see cref="RuleConditionConverter"/> builds for the rule leftovers of TASK-864. The rows
/// each shape selects on every provider are checked against the C# lambda by the shared <c>RuleLeftoversContract</c>; these
/// pin the conversion.
/// </summary>
public class Task882RuleConditionTests
{
    public enum Kind
    {
        Pending = 0,
        Active = 1,
    }

    [Table("T882Shapes")]
    public class Row : AbstractModel
    {
        public string? Text { get; set; }
        public int Number { get; set; }
        public Kind State { get; set; }
        public Guid Ref { get; set; }
        public DateTime At { get; set; }
    }

    private static Condition Leaf(IRule rule, bool typed = true)
    {
        var conditions = typed ? RuleConditionConverter.ToConditions<Row>(rule) : RuleConditionConverter.ToConditions(rule);
        return Leaves(conditions).First(c => c.Type != ConditionType.IsNull);
    }

    /// <summary>The leaves, depth first: a negated leaf over a nullable column sits in a group beside its IS NULL leg.</summary>
    private static IEnumerable<Condition> Leaves(IEnumerable<Condition> conditions)
        => conditions.SelectMany(c => c.SubConditions?.Any() == true ? Leaves(c.SubConditions) : new[] { c });

    private static bool MatchesNothing(IRule rule)
        => AbstractConnectorBase.ConstantTruth(RuleConditionConverter.ToConditions<Row>(rule).Single()) == false;

    // ── 1. null against a non-nullable column ──

    [Fact]
    public void A_between_with_a_null_bound_is_C_sharp_s_lifted_ordering_not_a_refusal()
    {
        var plain = new Rule("Number", ComparisonOperator.Between, null) { UpperValue = 5 };
        AbstractConnectorBase.ConstantTruth(RuleConditionConverter.ToConditions<Row>(plain).Single()).Should().BeFalse();

        var negated = new Rule("Number", ComparisonOperator.Between, 3) { UpperValue = null, IsNegated = true };
        AbstractConnectorBase.ConstantTruth(RuleConditionConverter.ToConditions<Row>(negated).Single())
            .Should().BeTrue("NOT (x >= 3 AND x <= null) is true on every row in C#");
    }

    // ── 2. Like ──

    [Theory]
    [InlineData("a%b")]
    [InlineData("%a%b%")]
    public void An_interior_wildcard_is_refused_by_name(string pattern)
    {
        Action typed = () => RuleConditionConverter.ToConditions<Row>(new Rule("Text", ComparisonOperator.Like, pattern)).ToList();
        typed.Should().Throw<NotSupportedException>().WithMessage("*'Text'*interior*");
        Action typeless = () => RuleConditionConverter.ToConditions(new Rule("Text", ComparisonOperator.Like, pattern)).ToList();
        typeless.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("abc", ConditionType.TextEqual, "abc")]
    [InlineData("%abc%", ConditionType.Like, "abc")]
    [InlineData("abc%", ConditionType.StartsWith, "abc")]
    [InlineData("%abc", ConditionType.EndsWith, "abc")]
    [InlineData("a%%", ConditionType.StartsWith, "a")]
    public void Like_maps_its_anchors_and_ignores_case_ordinally(string pattern, ConditionType type, string term)
    {
        var leaf = Leaf(new Rule("Text", ComparisonOperator.Like, pattern));

        leaf.Type.Should().Be(type);
        leaf.Values!.Cast<object>().Should().Equal(term);
        leaf.Comparison.Should().Be(StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.NotContains)]
    [InlineData(ComparisonOperator.StartsWith)]
    [InlineData(ComparisonOperator.EndsWith)]
    public void A_string_operator_ignores_case_ordinally_as_the_compiled_rule(ComparisonOperator op)
    {
        Leaf(new Rule("Text", op, "Ab")).Comparison.Should().Be(StringComparison.OrdinalIgnoreCase, "typed");
        Leaf(new Rule("Text", op, "Ab"), typed: false).Comparison.Should().Be(StringComparison.OrdinalIgnoreCase, "type-less");
    }

    [Theory]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.NotContains)]
    [InlineData(ComparisonOperator.Like)]
    public void A_string_operator_on_a_column_without_text_matches_nothing(ComparisonOperator op)
    {
        MatchesNothing(new Rule("Number", op, "%1%")).Should().BeTrue();
        MatchesNothing(new Rule("Number", op, "%1%") { IsNegated = true }).Should().BeTrue("never negated into every row");
    }

    // ── 3. values in the column's stored form ──

    [Fact]
    public void An_enum_given_by_name_or_number_binds_its_stored_number()
    {
        Leaf(new Rule("State", ComparisonOperator.Equal, "active")).Values!.Cast<object>().Should().Equal(1);
        Leaf(new Rule("State", ComparisonOperator.Equal, 1)).Values!.Cast<object>().Should().Equal(1);
        Leaf(new Rule("State", ComparisonOperator.In, new object[] { "Pending", Kind.Active })).Values!.Cast<object>().Should().Equal(0, 1);
    }

    [Fact]
    public void A_guid_or_a_date_given_as_text_binds_the_typed_value()
    {
        var guid = Guid.NewGuid();
        Leaf(new Rule("Ref", ComparisonOperator.Equal, guid.ToString("N"))).Values!.Cast<object>().Should().Equal(guid);
        Leaf(new Rule("At", ComparisonOperator.GreaterThan, "2026-01-02")).Values!.Cast<object>()
            .Should().Equal(new DateTime(2026, 1, 2));
    }

    [Fact]
    public void A_fraction_against_an_integral_column_is_bound_as_the_number_it_is()
        => Leaf(new Rule("Number", ComparisonOperator.GreaterThan, 2.5)).Values!.Cast<object>().Should().Equal(2.5);

    [Theory]
    [InlineData("State", "Bogus")]
    [InlineData("Ref", "not-a-guid")]
    [InlineData("Number", "abc")]
    public void A_value_without_a_form_for_the_column_matches_nothing_under_any_negation(string field, string value)
    {
        MatchesNothing(new Rule(field, ComparisonOperator.Equal, value)).Should().BeTrue();
        MatchesNothing(new Rule(field, ComparisonOperator.NotEqual, value)).Should().BeTrue();
        MatchesNothing(new Rule(field, ComparisonOperator.In, new object[] { value }) { IsNegated = true }).Should().BeTrue();
    }

    [Fact]
    public void Without_an_entity_type_a_value_is_bound_as_given()
        => Leaf(new Rule("State", ComparisonOperator.Equal, "Active"), typed: false).Values!.Cast<object>().Should().Equal("Active");
}
