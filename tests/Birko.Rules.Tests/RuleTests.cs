using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleTests
{
    [Fact]
    public void Rule_Constructor_SetsFieldOperatorValue()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100);

        rule.Field.Should().Be("Temperature");
        rule.Operator.Should().Be(ComparisonOperator.GreaterThan);
        rule.Value.Should().Be(100);
    }

    [Fact]
    public void Rule_Defaults_AreCorrect()
    {
        var rule = new Rule("Field", ComparisonOperator.Equal, "value");

        rule.IsEnabled.Should().BeTrue();
        rule.IsNegated.Should().BeFalse();
        rule.Severity.Should().Be(RuleSeverity.Info);
        rule.Name.Should().BeNull();
        rule.Description.Should().BeNull();
        rule.UpperValue.Should().BeNull();
    }

    [Fact]
    public void Rule_Between_Factory_SetsUpperValue()
    {
        var rule = Rule.Between("Temperature", 20, 40);

        rule.Field.Should().Be("Temperature");
        rule.Operator.Should().Be(ComparisonOperator.Between);
        rule.Value.Should().Be(20);
        rule.UpperValue.Should().Be(40);
    }

    [Fact]
    public void RuleGroup_And_Factory_CreatesAndGroup()
    {
        var r1 = new Rule("A", ComparisonOperator.Equal, 1);
        var r2 = new Rule("B", ComparisonOperator.Equal, 2);
        var group = RuleGroup.And(r1, r2);

        group.Logic.Should().Be(LogicOperator.And);
        group.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void RuleGroup_Or_Factory_CreatesOrGroup()
    {
        var r1 = new Rule("A", ComparisonOperator.Equal, 1);
        var group = RuleGroup.Or(r1);

        group.Logic.Should().Be(LogicOperator.Or);
        group.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void RuleGroup_Defaults_AreCorrect()
    {
        var group = RuleGroup.And();

        group.IsEnabled.Should().BeTrue();
        group.Severity.Should().Be(RuleSeverity.Info);
    }

    [Fact]
    public void RuleSet_Constructor_SetsName()
    {
        var set = new RuleSet("Test Set");

        set.Name.Should().Be("Test Set");
        set.IsEnabled.Should().BeTrue();
        set.Rules.Should().BeEmpty();
    }

    [Fact]
    public void RuleSet_ConstructorWithRules_AddsRules()
    {
        var r1 = new Rule("A", ComparisonOperator.Equal, 1);
        var set = new RuleSet("Test", r1);

        set.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void RuleResult_Match_SetsIsMatchTrue()
    {
        var rule = new Rule("F", ComparisonOperator.Equal, 1);
        var result = RuleResult.Match(rule, 42);

        result.IsMatch.Should().BeTrue();
        result.Rule.Should().BeSameAs(rule);
        result.ActualValue.Should().Be(42);
        result.Severity.Should().Be(rule.Severity);
    }

    [Fact]
    public void RuleResult_NoMatch_SetsIsMatchFalse()
    {
        var rule = new Rule("F", ComparisonOperator.Equal, 1);
        var result = RuleResult.NoMatch(rule, 99);

        result.IsMatch.Should().BeFalse();
        result.ActualValue.Should().Be(99);
    }
}
