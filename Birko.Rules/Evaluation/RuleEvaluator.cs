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

        // Negate only an answer the comparison could actually give. A `false` meaning "this operator does
        // not apply to this member" must NOT invert to a match — that is how a string operator on an int
        // became "every row" on the expression side (SH-H043/SH-H044, TASK-116), and this engine had the
        // identical hole: `IsNegated ? !match : match` treated both kinds of false alike.
        if (rule.IsNegated && ComparisonHelper.CanEvaluate(actual, rule.Operator))
            match = !match;

        return match ? RuleResult.Match(rule, actual) : RuleResult.NoMatch(rule, actual);
    }

    private RuleResult EvaluateGroup(RuleGroup group, IRuleContext context)
    {
        if (group.Rules.Count == 0)
            return RuleResult.NoMatch(group);

        // Skip disabled children so a disabled child neither fails an AND group nor is otherwise
        // treated as a non-match — mirroring RuleExpressionConverter.BuildGroupExpression, which
        // drops disabled children from the AndAlso/OrElse combination (the two paths must agree).
        var evaluatedAny = false;

        if (group.Logic == LogicOperator.And)
        {
            foreach (var child in group.Rules)
            {
                if (!child.IsEnabled)
                    continue;

                evaluatedAny = true;
                var result = Evaluate(child, context);
                if (!result.IsMatch)
                    return RuleResult.NoMatch(group);
            }
            // An empty or all-disabled group carries no effective constraint; treated as NoMatch,
            // consistent with the empty-group rule above.
            return evaluatedAny ? RuleResult.Match(group) : RuleResult.NoMatch(group);
        }
        else // Or
        {
            foreach (var child in group.Rules)
            {
                if (!child.IsEnabled)
                    continue;

                var result = Evaluate(child, context);
                if (result.IsMatch)
                    return RuleResult.Match(group, result.ActualValue);
            }
            return RuleResult.NoMatch(group);
        }
    }
}
