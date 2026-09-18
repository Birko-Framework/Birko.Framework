using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// CR-H134: a disabled child must be skipped in a group (not fail an AND), so the in-memory
/// evaluator and the LINQ expression converter agree.
/// CR-H135: the converter's static property cache must be thread-safe.
/// </summary>
public class DisabledRuleAndCacheTests
{
    private sealed class TestObj
    {
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    private readonly RuleEvaluator _evaluator = new();

    // ── CR-H134: disabled child in an AND group ──────────

    [Fact]
    public void And_DisabledChild_IsSkipped_NotFailed()
    {
        var group = new RuleGroup(
            LogicOperator.And,
            new Rule("Price", ComparisonOperator.GreaterThan, 0m),
            new Rule("Stock", ComparisonOperator.LessThan, 0) { IsEnabled = false });

        var obj = new TestObj { Price = 10m, Stock = 5 };
        var result = _evaluator.Evaluate(group, new ObjectRuleContext<TestObj>(obj));

        result.IsMatch.Should().BeTrue("the disabled Stock rule must be ignored, not fail the AND");
    }

    [Fact]
    public void And_DisabledChild_EvaluatorAndConverterAgree()
    {
        var group = new RuleGroup(
            LogicOperator.And,
            new Rule("Price", ComparisonOperator.GreaterThan, 0m),
            new Rule("Stock", ComparisonOperator.LessThan, 0) { IsEnabled = false });

        var obj = new TestObj { Price = 10m, Stock = 5 };

        var evaluatorMatch = _evaluator.Evaluate(group, new ObjectRuleContext<TestObj>(obj)).IsMatch;
        var predicate = RuleExpressionConverter.ToExpression<TestObj>(group)!.Compile();

        evaluatorMatch.Should().BeTrue();
        predicate(obj).Should().Be(evaluatorMatch, "in-memory and LINQ paths must produce the same result");
    }

    [Fact]
    public void And_EnabledChildStillFailsGroup()
    {
        var group = new RuleGroup(
            LogicOperator.And,
            new Rule("Price", ComparisonOperator.GreaterThan, 0m),
            new Rule("Stock", ComparisonOperator.LessThan, 0)); // enabled, will not match

        var obj = new TestObj { Price = 10m, Stock = 5 };
        _evaluator.Evaluate(group, new ObjectRuleContext<TestObj>(obj)).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void And_AllChildrenDisabled_IsNoMatch()
    {
        var group = new RuleGroup(
            LogicOperator.And,
            new Rule("Price", ComparisonOperator.GreaterThan, 0m) { IsEnabled = false },
            new Rule("Stock", ComparisonOperator.LessThan, 0) { IsEnabled = false });

        var obj = new TestObj { Price = 10m, Stock = 5 };
        _evaluator.Evaluate(group, new ObjectRuleContext<TestObj>(obj)).IsMatch.Should().BeFalse();
    }

    // ── CR-H135: concurrent property-cache access ────────

    [Fact]
    public void ToExpression_ConcurrentCalls_DoNotCorruptPropertyCache()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 0m);

        var act = () => Parallel.For(0, 500, _ =>
        {
            var predicate = RuleExpressionConverter.ToExpression<TestObj>(rule)!.Compile();
            predicate(new TestObj { Price = 5m }).Should().BeTrue();
        });

        act.Should().NotThrow("the property cache must be thread-safe under concurrent resolution");
    }
}
