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
/// TASK-864 — the conditions <see cref="RuleConditionConverter"/> builds carry TASK-859's nullable operands, so the one
/// producer of IS NULL legs (<c>ApplyCSharpNullSemantics</c>) gives a rule C#'s answer on a NULL row. The rows each shape
/// selects are checked against the database by the shared <c>RuleNullContract</c>; these pin the condition shapes and the
/// whole-table guard's verdict on them.
/// </summary>
public class Task864RuleNullConditionTests
{
    [Table("T864Shapes")]
    public class Row : AbstractModel
    {
        public int? Score { get; set; }

        public int Amount { get; set; }

        [RequiredField]
        public string Code { get; set; } = "";
    }

    private static List<Condition> Convert(IRule rule) => RuleConditionConverter.ToConditions<Row>(rule).ToList();

    private static Rule R(string field, ComparisonOperator op, object? value, bool negated = false)
        => new(field, op, value) { IsNegated = negated };

    private static Condition Leg(Condition term, string column)
    {
        term.IsOr.Should().BeTrue("the null leg is ORed onto the comparison");
        var subs = term.SubConditions!.ToList();
        subs.Should().HaveCount(2);
        subs[1].Type.Should().Be(ConditionType.IsNull);
        subs[1].IsNot.Should().BeFalse();
        subs[1].Name.Should().Be(column);
        return subs[0];
    }

    [Theory]
    [InlineData(ComparisonOperator.NotEqual, false, ConditionType.Equal)]
    [InlineData(ComparisonOperator.Equal, true, ConditionType.Equal)]
    [InlineData(ComparisonOperator.GreaterThan, true, ConditionType.Greather)]
    [InlineData(ComparisonOperator.LessThanOrEqual, true, ConditionType.LessAndEqual)]
    [InlineData(ComparisonOperator.NotIn, false, ConditionType.In)]
    [InlineData(ComparisonOperator.In, true, ConditionType.In)]
    public void A_comparison_C_sharp_answers_true_on_null_gains_the_IS_NULL_leg(ComparisonOperator op, bool negated, ConditionType type)
    {
        var value = op is ComparisonOperator.In or ComparisonOperator.NotIn ? (object)new object[] { 3, 5 } : 5;
        var conditions = Convert(R("Score", op, value, negated));

        conditions.Should().HaveCount(1);
        var compared = Leg(conditions[0], "T864Shapes.Score");
        compared.Type.Should().Be(type);
        compared.IsNot.Should().BeTrue();
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, false)]
    [InlineData(ComparisonOperator.NotEqual, true)]
    [InlineData(ComparisonOperator.GreaterThan, false)]
    [InlineData(ComparisonOperator.In, false)]
    [InlineData(ComparisonOperator.NotIn, true)]
    public void A_comparison_C_sharp_answers_false_on_null_keeps_its_SQL(ComparisonOperator op, bool negated)
    {
        var value = op is ComparisonOperator.In or ComparisonOperator.NotIn ? (object)new object[] { 3, 5 } : 5;
        var conditions = Convert(R("Score", op, value, negated));

        conditions.Should().ContainSingle();
        conditions[0].SubConditions.Should().BeNullOrEmpty();
        conditions[0].IsNot.Should().BeFalse();
    }

    [Theory]
    [InlineData("Amount")]
    [InlineData("Code")]
    [InlineData("Guid")]
    public void A_column_that_cannot_hold_null_renders_unchanged(string field)
    {
        object value = field switch { "Code" => "a", "Guid" => Guid.Empty, _ => 5 };
        var conditions = Convert(R(field, ComparisonOperator.NotEqual, value));

        conditions.Should().ContainSingle();
        conditions[0].SubConditions.Should().BeNullOrEmpty("a non-nullable, [RequiredField] or primary column has no NULL row");
        conditions[0].Type.Should().Be(ConditionType.Equal);
        conditions[0].IsNot.Should().BeTrue();
        conditions[0].NullableOperands.Should().BeNull();
    }

    [Fact]
    public void A_negated_between_negates_each_bound_and_each_gains_the_leg()
    {
        var rule = Rule.Between("Score", 3, 5);
        rule.IsNegated = true;

        // A top-level OR group is wrapped in an AND term (TASK-559), so it never joins a sibling by its own IsOr.
        var group = Convert(rule).Should().ContainSingle().Subject.SubConditions!.Single();
        group.IsOr.Should().BeTrue();
        var bounds = group.SubConditions!.ToList();
        bounds.Should().HaveCount(2);
        var lower = Leg(bounds[0], "T864Shapes.Score");
        lower.Type.Should().Be(ConditionType.GreatherAndEqual);
        lower.IsNot.Should().BeTrue();
        var upper = Leg(bounds[1], "T864Shapes.Score");
        upper.Type.Should().Be(ConditionType.LessAndEqual);
        upper.IsNot.Should().BeTrue();
    }

    [Fact]
    public void In_with_a_null_element_takes_it_out_of_the_list_and_matches_the_NULL_row()
    {
        var compared = Leg(Convert(R("Score", ComparisonOperator.In, new object?[] { null, 5 })).Single(), "T864Shapes.Score");
        compared.Type.Should().Be(ConditionType.In);
        compared.IsNot.Should().BeFalse();
        compared.Values!.Cast<object?>().Should().Equal(5);

        var notIn = Convert(R("Score", ComparisonOperator.NotIn, new object?[] { null, 5 })).Single();
        notIn.SubConditions.Should().BeNullOrEmpty("NOT IN (5) already drops the NULL row, which C# excludes");
        notIn.Values!.Cast<object?>().Should().Equal(5);
    }

    [Theory]
    [InlineData(ComparisonOperator.In, false)]
    [InlineData(ComparisonOperator.NotIn, true)]
    public void A_list_of_only_null_is_IS_NULL(ComparisonOperator op, bool isNot)
    {
        var condition = Convert(R("Score", op, new object?[] { null })).Single();
        condition.Type.Should().Be(ConditionType.IsNull);
        condition.IsNot.Should().Be(isNot);
    }

    [Theory]
    [InlineData(ComparisonOperator.In, false)]
    [InlineData(ComparisonOperator.In, true)]
    [InlineData(ComparisonOperator.NotIn, false)]
    [InlineData(ComparisonOperator.NotIn, true)]
    [InlineData(ComparisonOperator.Contains, true)]
    [InlineData(ComparisonOperator.NotContains, false)]
    [InlineData(ComparisonOperator.StartsWith, true)]
    [InlineData(ComparisonOperator.Like, true)]
    public void A_null_set_or_needle_matches_nothing_under_any_negation(ComparisonOperator op, bool negated)
    {
        var field = op is ComparisonOperator.In or ComparisonOperator.NotIn ? "Score" : "Code";
        var condition = Convert(R(field, op, null, negated)).Single();

        AbstractConnectorBase.ConstantTruth(condition).Should().BeFalse("NotIn null used to bind nothing and read every row");
    }

    [Theory]
    [InlineData(ComparisonOperator.GreaterThan)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual)]
    [InlineData(ComparisonOperator.LessThan)]
    [InlineData(ComparisonOperator.LessThanOrEqual)]
    public void An_ordering_against_null_is_C_sharp_s_constant_and_the_guard_sees_it(ComparisonOperator op)
    {
        var plain = Convert(R("Score", op, null)).Single();
        AbstractConnectorBase.ConstantTruth(plain).Should().BeFalse();

        var negated = Convert(R("Score", op, null, negated: true)).Single();
        AbstractConnectorBase.ConstantTruth(negated).Should().BeTrue("a whole-table write must be refused");

        // Inside a group the reduction carries through: NOT (Score > null AND Amount > 3) is every row.
        var group = new RuleGroup(LogicOperator.And, R("Score", op, null), R("Amount", ComparisonOperator.GreaterThan, 3))
        {
            IsNegated = true,
        };
        AbstractConnectorBase.ConstantTruth(Convert(group).Single()).Should().BeTrue();
    }

    [Fact]
    public void Equal_null_stays_IS_NULL()
    {
        var condition = Convert(R("Score", ComparisonOperator.Equal, null)).Single();
        condition.Type.Should().Be(ConditionType.IsNull);
        condition.IsNot.Should().BeFalse();

        var notEqual = Convert(R("Score", ComparisonOperator.NotEqual, null)).Single();
        notEqual.Type.Should().Be(ConditionType.IsNull);
        notEqual.IsNot.Should().BeTrue();
    }

    [Fact]
    public void A_rule_set_gets_the_legs_too()
    {
        var conditions = RuleConditionConverter.ToConditions<Row>(new RuleSet("S",
            R("Score", ComparisonOperator.NotEqual, 5), R("Amount", ComparisonOperator.Equal, 1))).ToList();

        conditions.Should().HaveCount(2);
        Leg(conditions[0], "T864Shapes.Score").IsNot.Should().BeTrue();
        conditions[1].SubConditions.Should().BeNullOrEmpty();
        conditions[1].IsOr.Should().BeFalse("the second AND term must still join with AND");
    }
}
