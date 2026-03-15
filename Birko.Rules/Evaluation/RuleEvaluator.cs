using System.Collections.Generic;
using System.Linq;

namespace Birko.Rules;

/// <summary>
/// Default rule evaluator. Stateless, singleton-safe.
/// </summary>
public class RuleEvaluator : IRuleEvaluator
{
    public RuleResult Evaluate(IRule rule, IRuleContext context)
    {
        if (!rule.IsEnabled)
            return RuleResult.NoMatch(rule);

        return rule switch
        {
            Rule leaf => EvaluateLeaf(leaf, context),
            RuleGroup group => EvaluateGroup(group, context),
            _ => RuleResult.NoMatch(rule)
        };
    }

    public IReadOnlyList<RuleResult> EvaluateAll(IEnumerable<IRule> rules, IRuleContext context)
    {
        return rules.Select(r => Evaluate(r, context)).ToList();
    }

    public IReadOnlyList<RuleResult> EvaluateMatches(IEnumerable<IRule> rules, IRuleContext context)
    {
        return rules
            .Select(r => Evaluate(r, context))
            .Where(r => r.IsMatch)
            .ToList();
    }

    public IReadOnlyList<RuleResult> Evaluate(RuleSet ruleSet, IRuleContext context)
    {
        if (!ruleSet.IsEnabled)
            return [];

        return EvaluateMatches(ruleSet.Rules, context);
    }

    private static RuleResult EvaluateLeaf(Rule rule, IRuleContext context)
    {
        // IsNull/IsNotNull don't need the field to exist
        if (rule.Operator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
        {
            context.TryGetValue(rule.Field, out var nullCheckValue);
            var nullResult = ComparisonHelper.Compare(nullCheckValue, rule.Operator, null);
            nullResult = rule.IsNegated ? !nullResult : nullResult;
            return nullResult ? RuleResult.Match(rule, nullCheckValue) : RuleResult.NoMatch(rule, nullCheckValue);
        }

        // Field must exist for other operators
        if (!context.TryGetValue(rule.Field, out var actual))
            return RuleResult.NoMatch(rule);

        var match = ComparisonHelper.Compare(actual, rule.Operator, rule.Value, rule.UpperValue);
        match = rule.IsNegated ? !match : match;

        return match ? RuleResult.Match(rule, actual) : RuleResult.NoMatch(rule, actual);
    }

    private RuleResult EvaluateGroup(RuleGroup group, IRuleContext context)
    {
        if (group.Rules.Count == 0)
            return RuleResult.NoMatch(group);

        if (group.Logic == LogicOperator.And)
        {
            foreach (var child in group.Rules)
            {
                var result = Evaluate(child, context);
                if (!result.IsMatch)
                    return RuleResult.NoMatch(group);
            }
            return RuleResult.Match(group);
        }
        else // Or
        {
            foreach (var child in group.Rules)
            {
                var result = Evaluate(child, context);
                if (result.IsMatch)
                    return RuleResult.Match(group, result.ActualValue);
            }
            return RuleResult.NoMatch(group);
        }
    }
}
