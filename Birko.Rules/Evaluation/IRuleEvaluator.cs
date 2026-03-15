using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// Evaluates rules against a context. Stateless — safe for singleton DI.
/// </summary>
public interface IRuleEvaluator
{
    /// <summary>
    /// Evaluate a single rule (leaf or group) against a context.
    /// </summary>
    RuleResult Evaluate(IRule rule, IRuleContext context);

    /// <summary>
    /// Evaluate all rules and return results (including non-matches).
    /// </summary>
    IReadOnlyList<RuleResult> EvaluateAll(IEnumerable<IRule> rules, IRuleContext context);

    /// <summary>
    /// Evaluate all rules and return only matches.
    /// </summary>
    IReadOnlyList<RuleResult> EvaluateMatches(IEnumerable<IRule> rules, IRuleContext context);

    /// <summary>
    /// Evaluate a RuleSet (respects IsEnabled on set and individual rules).
    /// </summary>
    IReadOnlyList<RuleResult> Evaluate(RuleSet ruleSet, IRuleContext context);
}
