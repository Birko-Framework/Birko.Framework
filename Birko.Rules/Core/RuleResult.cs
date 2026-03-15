using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// Outcome of evaluating a rule against a context.
/// </summary>
public class RuleResult
{
    /// <summary>
    /// Whether the rule condition was satisfied.
    /// </summary>
    public bool IsMatch { get; }

    /// <summary>
    /// The rule that was evaluated.
    /// </summary>
    public IRule Rule { get; }

    /// <summary>
    /// Severity from the matched rule (meaningful only when IsMatch is true).
    /// </summary>
    public RuleSeverity Severity => Rule.Severity;

    /// <summary>
    /// The actual field value that was evaluated (for diagnostics/audit).
    /// </summary>
    public object? ActualValue { get; }

    /// <summary>
    /// Optional metadata from evaluation (e.g., which sub-rule matched in a group).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    private RuleResult(bool isMatch, IRule rule, object? actualValue)
    {
        IsMatch = isMatch;
        Rule = rule;
        ActualValue = actualValue;
    }

    public static RuleResult Match(IRule rule, object? actualValue = null) => new(true, rule, actualValue);
    public static RuleResult NoMatch(IRule rule, object? actualValue = null) => new(false, rule, actualValue);
}
