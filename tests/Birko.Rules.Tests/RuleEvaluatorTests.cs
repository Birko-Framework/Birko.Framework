using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleEvaluatorTests
{
    private readonly RuleEvaluator _evaluator = new();

    private static IRuleContext Ctx(params (string field, object? value)[] values)
        => DictionaryRuleContext.From(values);

    // ── Leaf: Equality ──

    [Fact]
    public void Equal_Match()
    {
        var rule = new Rule("Status", ComparisonOperator.Equal, "Active");
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Active")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Equal_NoMatch()
    {
        var rule = new Rule("Status", ComparisonOperator.Equal, "Active");
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Inactive")));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Equal_FloatPromotedToDouble_MatchesWithinTolerance()
    {
        // CR-L333: a float field (0.1f) promoted to double must compare equal to the double literal 0.1 —
        // the old `< double.Epsilon` was effectively exact equality and this did not match.
        var rule = new Rule("Ratio", ComparisonOperator.Equal, 0.1);
        var result = _evaluator.Evaluate(rule, Ctx(("Ratio", 0.1f)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Equal_DistinctDoubles_DoNotMatch()
    {
        // The scaled tolerance must still reject values that genuinely differ.
        var rule = new Rule("Ratio", ComparisonOperator.Equal, 0.1);
        var result = _evaluator.Evaluate(rule, Ctx(("Ratio", 0.2)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Equal_LargeIntegersDifferingByOne_DoNotMatch()
    {
        // CR-L333: integral equality stays exact — the fractional tolerance must NOT loosen it (a flat
        // relative tolerance would wrongly equate 1e9 and 1e9+1).
        var rule = new Rule("Id", ComparisonOperator.Equal, 1_000_000_000L);
        var result = _evaluator.Evaluate(rule, Ctx(("Id", 1_000_000_001L)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void NotEqual_Match()
    {
        var rule = new Rule("Status", ComparisonOperator.NotEqual, "Active");
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Inactive")));
        result.IsMatch.Should().BeTrue();
    }

    // ── Leaf: Numeric comparisons ──

    [Fact]
    public void GreaterThan_Match()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 105)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void GreaterThan_NoMatch_WhenEqual()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 100)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void GreaterThanOrEqual_Match_WhenEqual()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThanOrEqual, 100);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 100)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void LessThan_Match()
    {
        var rule = new Rule("Temperature", ComparisonOperator.LessThan, 50);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 30)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void LessThanOrEqual_Match_WhenEqual()
    {
        var rule = new Rule("Temperature", ComparisonOperator.LessThanOrEqual, 50);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 50)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void NumericPromotion_IntVsDouble()
    {
        var rule = new Rule("Value", ComparisonOperator.Equal, 42.0);
        var result = _evaluator.Evaluate(rule, Ctx(("Value", 42)));
        result.IsMatch.Should().BeTrue();
    }

    // ── Leaf: Between ──

    [Fact]
    public void Between_Match_InRange()
    {
        var rule = Rule.Between("Temperature", 20, 40);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 30)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Between_Match_AtBoundaries()
    {
        var rule = Rule.Between("Temperature", 20, 40);
        _evaluator.Evaluate(rule, Ctx(("Temperature", 20))).IsMatch.Should().BeTrue();
        _evaluator.Evaluate(rule, Ctx(("Temperature", 40))).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Between_NoMatch_OutOfRange()
    {
        var rule = Rule.Between("Temperature", 20, 40);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 50)));
        result.IsMatch.Should().BeFalse();
    }

    // ── Leaf: Null checks ──

    [Fact]
    public void IsNull_Match_WhenNull()
    {
        var rule = new Rule("Field", ComparisonOperator.IsNull, null);
        var result = _evaluator.Evaluate(rule, Ctx(("Field", (object?)null)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void IsNull_Match_WhenFieldMissing()
    {
        var rule = new Rule("Missing", ComparisonOperator.IsNull, null);
        var result = _evaluator.Evaluate(rule, Ctx(("Other", 1)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void IsNotNull_Match_WhenHasValue()
    {
        var rule = new Rule("Field", ComparisonOperator.IsNotNull, null);
        var result = _evaluator.Evaluate(rule, Ctx(("Field", 42)));
        result.IsMatch.Should().BeTrue();
    }

    // ── Leaf: String operations ──

    [Fact]
    public void Contains_Match()
    {
        var rule = new Rule("Name", ComparisonOperator.Contains, "ell");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello World")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void NotContains_Match()
    {
        var rule = new Rule("Name", ComparisonOperator.NotContains, "xyz");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void StartsWith_Match()
    {
        var rule = new Rule("Name", ComparisonOperator.StartsWith, "Hel");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void EndsWith_Match()
    {
        var rule = new Rule("Name", ComparisonOperator.EndsWith, "rld");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "World")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Like_MiddleWildcard()
    {
        var rule = new Rule("Name", ComparisonOperator.Like, "%ell%");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Like_StartWildcard()
    {
        var rule = new Rule("Name", ComparisonOperator.Like, "%llo");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Like_EndWildcard()
    {
        var rule = new Rule("Name", ComparisonOperator.Like, "Hel%");
        var result = _evaluator.Evaluate(rule, Ctx(("Name", "Hello")));
        result.IsMatch.Should().BeTrue();
    }

    // ── Leaf: In / NotIn ──

    [Fact]
    public void In_Match()
    {
        var rule = new Rule("Status", ComparisonOperator.In, new[] { "Active", "Pending" });
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Active")));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void In_NoMatch()
    {
        var rule = new Rule("Status", ComparisonOperator.In, new[] { "Active", "Pending" });
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Deleted")));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void NotIn_Match()
    {
        var rule = new Rule("Status", ComparisonOperator.NotIn, new[] { "Deleted", "Archived" });
        var result = _evaluator.Evaluate(rule, Ctx(("Status", "Active")));
        result.IsMatch.Should().BeTrue();
    }

    // ── Negation ──

    [Fact]
    public void Negated_ReversesResult()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100) { IsNegated = true };
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 105)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Negated_NoMatch_BecomesMatch()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100) { IsNegated = true };
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 50)));
        result.IsMatch.Should().BeTrue();
    }

    // ── Disabled rules ──

    [Fact]
    public void DisabledRule_ReturnsNoMatch()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 0) { IsEnabled = false };
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 100)));
        result.IsMatch.Should().BeFalse();
    }

    // ── Missing field ──

    [Fact]
    public void MissingField_ReturnsNoMatch()
    {
        var rule = new Rule("Missing", ComparisonOperator.Equal, 1);
        var result = _evaluator.Evaluate(rule, Ctx(("Other", 1)));
        result.IsMatch.Should().BeFalse();
    }

    // ── Groups ──

    [Fact]
    public void AndGroup_AllMatch_ReturnsMatch()
    {
        var group = RuleGroup.And(
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 2)
        );
        var result = _evaluator.Evaluate(group, Ctx(("A", 1), ("B", 2)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void AndGroup_OneNoMatch_ReturnsNoMatch()
    {
        var group = RuleGroup.And(
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 999)
        );
        var result = _evaluator.Evaluate(group, Ctx(("A", 1), ("B", 2)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void OrGroup_OneMatch_ReturnsMatch()
    {
        var group = RuleGroup.Or(
            new Rule("A", ComparisonOperator.Equal, 999),
            new Rule("B", ComparisonOperator.Equal, 2)
        );
        var result = _evaluator.Evaluate(group, Ctx(("A", 1), ("B", 2)));
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void OrGroup_NoneMatch_ReturnsNoMatch()
    {
        var group = RuleGroup.Or(
            new Rule("A", ComparisonOperator.Equal, 999),
            new Rule("B", ComparisonOperator.Equal, 999)
        );
        var result = _evaluator.Evaluate(group, Ctx(("A", 1), ("B", 2)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void EmptyGroup_ReturnsNoMatch()
    {
        var group = RuleGroup.And();
        var result = _evaluator.Evaluate(group, Ctx(("A", 1)));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void NestedGroups_EvaluateCorrectly()
    {
        // (A == 1 AND B == 2) OR (C == 3)
        var group = RuleGroup.Or(
            RuleGroup.And(
                new Rule("A", ComparisonOperator.Equal, 1),
                new Rule("B", ComparisonOperator.Equal, 2)
            ),
            new Rule("C", ComparisonOperator.Equal, 3)
        );

        _evaluator.Evaluate(group, Ctx(("A", 1), ("B", 2), ("C", 0))).IsMatch.Should().BeTrue();
        _evaluator.Evaluate(group, Ctx(("A", 0), ("B", 0), ("C", 3))).IsMatch.Should().BeTrue();
        _evaluator.Evaluate(group, Ctx(("A", 0), ("B", 0), ("C", 0))).IsMatch.Should().BeFalse();
    }

    // ── EvaluateAll / EvaluateMatches ──

    [Fact]
    public void EvaluateAll_ReturnsAllResults()
    {
        var rules = new IRule[]
        {
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 999)
        };
        var results = _evaluator.EvaluateAll(rules, Ctx(("A", 1), ("B", 2)));
        results.Should().HaveCount(2);
        results[0].IsMatch.Should().BeTrue();
        results[1].IsMatch.Should().BeFalse();
    }

    [Fact]
    public void EvaluateMatches_ReturnsOnlyMatches()
    {
        var rules = new IRule[]
        {
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 999)
        };
        var results = _evaluator.EvaluateMatches(rules, Ctx(("A", 1), ("B", 2)));
        results.Should().HaveCount(1);
        results[0].IsMatch.Should().BeTrue();
    }

    // ── RuleSet ──

    [Fact]
    public void RuleSet_Evaluate_ReturnsMatches()
    {
        var ruleSet = new RuleSet("Test",
            new Rule("A", ComparisonOperator.Equal, 1),
            new Rule("B", ComparisonOperator.Equal, 999)
        );
        var results = _evaluator.Evaluate(ruleSet, Ctx(("A", 1), ("B", 2)));
        results.Should().HaveCount(1);
    }

    [Fact]
    public void RuleSet_Disabled_ReturnsEmpty()
    {
        var ruleSet = new RuleSet("Test",
            new Rule("A", ComparisonOperator.Equal, 1)
        ) { IsEnabled = false };
        var results = _evaluator.Evaluate(ruleSet, Ctx(("A", 1)));
        results.Should().BeEmpty();
    }

    // ── ActualValue in result ──

    [Fact]
    public void Result_ContainsActualValue()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 50);
        var result = _evaluator.Evaluate(rule, Ctx(("Temperature", 75)));
        result.ActualValue.Should().Be(75);
    }
}
