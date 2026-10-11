using System.Linq;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// SH-H045 (TASK-320) — <c>RuleEvaluator</c> ignored <c>RuleGroup.IsNegated</c>, so a negated group answered
/// as the group itself. Negation is now pushed to the leaves (De Morgan), where an answer the comparison could
/// not give is never inverted — so a negated group cannot turn an unevaluable leaf into a match.
/// </summary>
public class RuleEvaluatorGroupNegationTests
{
    private readonly RuleEvaluator _evaluator = new();

    private static IRuleContext Ctx(params (string field, object? value)[] values)
        => DictionaryRuleContext.From(values);

    private static RuleGroup Group(LogicOperator logic, bool negated, params IRule[] rules)
        => new(logic, rules.ToList()) { IsNegated = negated };

    [Theory]
    [InlineData("Active", 5, false)] // the AND group holds → its negation does not
    [InlineData("Active", 1, true)]
    [InlineData("Draft", 5, true)]
    public void Negated_and_group_is_its_complement(string status, int quantity, bool expected)
    {
        var rule = Group(LogicOperator.And, negated: true,
            new Rule("Status", ComparisonOperator.Equal, "Active"),
            new Rule("Quantity", ComparisonOperator.GreaterThan, 2));

        _evaluator.Evaluate(rule, Ctx(("Status", status), ("Quantity", quantity))).IsMatch.Should().Be(expected);
    }

    [Theory]
    [InlineData("Active", false)]
    [InlineData("Draft", false)]
    [InlineData("Archived", true)]
    public void Negated_or_group_is_its_complement(string status, bool expected)
    {
        var rule = Group(LogicOperator.Or, negated: true,
            new Rule("Status", ComparisonOperator.Equal, "Active"),
            new Rule("Status", ComparisonOperator.Equal, "Draft"));

        _evaluator.Evaluate(rule, Ctx(("Status", status))).IsMatch.Should().Be(expected);
    }

    [Fact]
    public void An_unevaluable_leaf_in_a_negated_group_is_not_inverted_into_a_match()
    {
        // NOT(Quantity > 0 AND Quantity Contains "1"): Contains on an int cannot be evaluated, so it stays a
        // non-match under negation, leaving NOT(Quantity > 0) — false for 5. Inverting the group's result
        // would have answered true.
        var rule = Group(LogicOperator.And, negated: true,
            new Rule("Quantity", ComparisonOperator.GreaterThan, 0),
            new Rule("Quantity", ComparisonOperator.Contains, "1"));

        _evaluator.Evaluate(rule, Ctx(("Quantity", 5))).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void A_negated_empty_group_matches_nothing()
    {
        _evaluator.Evaluate(Group(LogicOperator.And, negated: true), Ctx(("Status", "Active"))).IsMatch.Should().BeFalse();
    }
}
