using Birko.Data.SQL.Conditions;
using Birko.Rules;
using FluentAssertions;
using System.Collections;
using System.Linq;
using Xunit;

namespace Birko.Data.SQL.Tests;

public class RuleConditionConverterTests
{
    // ── Leaf conversions ──

    [Fact]
    public void Equal_ConvertsToEqualCondition()
    {
        var rule = new Rule("Name", ComparisonOperator.Equal, "John");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions.Should().HaveCount(1);
        conditions[0].Name.Should().Be("Name");
        conditions[0].Type.Should().Be(ConditionType.Equal);
        conditions[0].IsNot.Should().BeFalse();
    }

    [Fact]
    public void NotEqual_ConvertsToEqualWithIsNot()
    {
        var rule = new Rule("Status", ComparisonOperator.NotEqual, "Deleted");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions.Should().HaveCount(1);
        conditions[0].Type.Should().Be(ConditionType.Equal);
        conditions[0].IsNot.Should().BeTrue();
    }

    [Fact]
    public void GreaterThan_ConvertsToGreather()
    {
        var rule = new Rule("Age", ComparisonOperator.GreaterThan, 18);
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.Greather);
    }

    [Fact]
    public void LessThanOrEqual_ConvertsToLessAndEqual()
    {
        var rule = new Rule("Price", ComparisonOperator.LessThanOrEqual, 100m);
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.LessAndEqual);
    }

    [Fact]
    public void IsNull_ConvertsToIsNullCondition()
    {
        var rule = new Rule("Email", ComparisonOperator.IsNull, null);
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.IsNull);
        conditions[0].IsNot.Should().BeFalse();
    }

    [Fact]
    public void IsNotNull_ConvertsToIsNullWithIsNot()
    {
        var rule = new Rule("Email", ComparisonOperator.IsNotNull, null);
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.IsNull);
        conditions[0].IsNot.Should().BeTrue();
    }

    [Fact]
    public void Contains_ConvertsToLike()
    {
        var rule = new Rule("Name", ComparisonOperator.Contains, "john");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.Like);
        conditions[0].IsNot.Should().BeFalse();
    }

    [Fact]
    public void NotContains_ConvertsToLikeWithIsNot()
    {
        var rule = new Rule("Name", ComparisonOperator.NotContains, "spam");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.Like);
        conditions[0].IsNot.Should().BeTrue();
    }

    [Fact]
    public void StartsWith_ConvertsToStartsWith()
    {
        var rule = new Rule("Name", ComparisonOperator.StartsWith, "A");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.StartsWith);
    }

    [Fact]
    public void EndsWith_ConvertsToEndsWith()
    {
        var rule = new Rule("Name", ComparisonOperator.EndsWith, "son");
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.EndsWith);
    }

    [Fact]
    public void In_ConvertsToInCondition()
    {
        var rule = new Rule("Status", ComparisonOperator.In, new[] { "Active", "Pending" });
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.In);
        conditions[0].IsNot.Should().BeFalse();
    }

    [Fact]
    public void NotIn_ConvertsToInWithIsNot()
    {
        var rule = new Rule("Status", ComparisonOperator.NotIn, new[] { "Deleted" });
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.In);
        conditions[0].IsNot.Should().BeTrue();
    }

    // ── Groups ──

    [Fact]
    public void OrGroup_ConvertsToSubCondition()
    {
        var group = RuleGroup.Or(
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 2)
        );
        var conditions = RuleConditionConverter.ToConditions(group).ToList();

        conditions.Should().HaveCount(1);
        conditions[0].SubConditions.Should().NotBeNull();
    }

    [Fact]
    public void AndGroup_ConvertsToSubCondition()
    {
        var group = RuleGroup.And(
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 2)
        );
        var conditions = RuleConditionConverter.ToConditions(group).ToList();

        conditions.Should().HaveCount(1);
        conditions[0].SubConditions.Should().NotBeNull();
    }

    // ── Disabled rules ──

    [Fact]
    public void DisabledRule_ReturnsEmpty()
    {
        var rule = new Rule("A", ComparisonOperator.Equal, 1) { IsEnabled = false };
        RuleConditionConverter.ToConditions(rule).Should().BeEmpty();
    }

    [Fact]
    public void DisabledRuleSet_ReturnsEmpty()
    {
        var ruleSet = new RuleSet("Test",
            new Rule("A", ComparisonOperator.Equal, 1)
        ) { IsEnabled = false };
        RuleConditionConverter.ToConditions(ruleSet).Should().BeEmpty();
    }

    // ── RuleSet ──

    [Fact]
    public void RuleSet_ConvertsAllEnabledRules()
    {
        var ruleSet = new RuleSet("Test",
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.GreaterThan, 10),
            new Rule("C", ComparisonOperator.Equal, 3) { IsEnabled = false }
        );
        var conditions = RuleConditionConverter.ToConditions(ruleSet).ToList();

        conditions.Should().HaveCount(2);
    }

    // ── Negation ──

    [Fact]
    public void NegatedEqual_ConvertsToIsNotTrue()
    {
        var rule = new Rule("A", ComparisonOperator.Equal, 1) { IsNegated = true };
        var conditions = RuleConditionConverter.ToConditions(rule).ToList();

        conditions[0].Type.Should().Be(ConditionType.Equal);
        conditions[0].IsNot.Should().BeTrue();
    }
}
