using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Rules;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Adapts a Birko.Rules rule tree into the Specification pattern.
/// Supports in-memory evaluation via ObjectRuleContext and LINQ expression generation for store queries.
/// </summary>
/// <typeparam name="T">The entity type to evaluate.</typeparam>
public class RuleSpecification<T> : Specification<T> where T : class
{
    private readonly IRule _rule;
    private readonly IRuleEvaluator _evaluator;

    public RuleSpecification(IRule rule, IRuleEvaluator? evaluator = null)
    {
        _rule = rule;
        _evaluator = evaluator ?? new RuleEvaluator();
    }

    public RuleSpecification(RuleSet ruleSet, IRuleEvaluator? evaluator = null)
        : this(WrapRuleSet(ruleSet), evaluator)
    {
    }

    /// <summary>
    /// The store filter, from <see cref="RuleExpressionConverter"/>: the framework's one rule-to-LINQ translator.
    /// </summary>
    /// <remarks>
    /// TASK-144 (SH-M299). This class carried its own copy of that translator, and the two drifted both ways (TASK-116
    /// found three operators missing here; this task found five fixes missing there). One translator now: a rule filters a
    /// store the same way whichever entry point a caller picked. Its degradation invariant (an untranslatable leaf matches
    /// nothing and is never negated) is the converter's.
    /// </remarks>
    public override Expression<Func<T, bool>> ToExpression() => RuleExpressionConverter.ToExpression<T>(_rule);

    /// <summary>
    /// Overrides the base compiled-expression evaluation to use the RuleEvaluator for in-memory
    /// evaluation (more accurate than the compiled expression for complex types). Must be an
    /// override (not `new`) so callers holding an ISpecification&lt;T&gt;/Specification&lt;T&gt;
    /// reference — including And/Or/Not combinators and collections — dispatch here (CR-H074).
    /// </summary>
    public override bool IsSatisfiedBy(T entity)
    {
        var context = new ObjectRuleContext<T>(entity);
        var result = _evaluator.Evaluate(_rule, context);
        return result.IsMatch;
    }

    private static IRule WrapRuleSet(RuleSet ruleSet)
    {
        if (!ruleSet.IsEnabled)
            return new RuleGroup(LogicOperator.And, new List<IRule>());

        return new RuleGroup(LogicOperator.And, ruleSet.Rules) { IsEnabled = ruleSet.IsEnabled };
    }
}
