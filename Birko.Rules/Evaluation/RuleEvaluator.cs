using System.Collections.Generic;
using System.Linq;

namespace Birko.Rules;

/// <summary>
/// Default rule evaluator. Stateless, singleton-safe.
/// </summary>
public class RuleEvaluator : IRuleEvaluator
{
    public RuleResult Evaluate(IRule rule, IRuleContext context) => Evaluate(rule, context, negate: false);

    /// <param name="negate">
    /// Whether an enclosing negated group inverts this rule. Negation is pushed down to the leaves (De Morgan),
    /// where an answer the comparison could not give is never inverted — inverting a whole group's result would
    /// turn such a leaf's "no" into a match (SH-H045; the leaf rule is SH-H043/SH-H044).
    /// </param>
    private RuleResult Evaluate(IRule rule, IRuleContext context, bool negate)
    {
        if (!rule.IsEnabled)
            return RuleResult.NoMatch(rule);

        return rule switch
        {
            Rule leaf => EvaluateLeaf(leaf, context, negate),
            RuleGroup group => EvaluateGroup(group, context, negate),
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

    private static RuleResult EvaluateLeaf(Rule rule, IRuleContext context, bool negate)
    {
        // The leaf's own flag and an enclosing negated group's cancel out: NOT(NOT a) is a.
        var negated = rule.IsNegated != negate;

        // IsNull/IsNotNull don't need the field to exist
        if (rule.Operator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
        {
            context.TryGetValue(rule.Field, out var nullCheckValue);
            var nullResult = ComparisonHelper.Compare(nullCheckValue, rule.Operator, null);
            nullResult = negated ? !nullResult : nullResult;
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
        if (negated && ComparisonHelper.CanEvaluate(actual, rule.Operator))
            match = !match;

        return match ? RuleResult.Match(rule, actual) : RuleResult.NoMatch(rule, actual);
    }

    /// <summary>
    /// Evaluates a group's enabled children; a negated group by De Morgan (see <c>Evaluate</c>'s <c>negate</c>).
    /// An empty or all-disabled group matches nothing, negated or not.
    /// </summary>
    private RuleResult EvaluateGroup(RuleGroup group, IRuleContext context, bool negate)
    {
        if (group.Rules.Count == 0)
            return RuleResult.NoMatch(group);

        var negated = group.IsNegated != negate;
        var conjunction = (group.Logic == LogicOperator.And) != negated;

        // Skip disabled children so a disabled child neither fails an AND group nor is otherwise
        // treated as a non-match — mirroring RuleExpressionConverter.BuildGroupExpression, which
        // drops disabled children from the AndAlso/OrElse combination (the two agree on disabled children;
        // on group negation the converter still negates naively — TASK-558).
        var evaluatedAny = false;

        if (conjunction)
        {
            foreach (var child in group.Rules)
            {
                if (!child.IsEnabled)
                    continue;

                evaluatedAny = true;
                var result = Evaluate(child, context, negated);
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

                var result = Evaluate(child, context, negated);
                if (result.IsMatch)
                    return RuleResult.Match(group, result.ActualValue);
            }
            return RuleResult.NoMatch(group);
        }
    }
}
