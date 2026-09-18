using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// Composite rule — AND/OR group of child rules (can be nested).
/// Aligned with Birko.Data.SQL.Condition.SubConditions + IsOr pattern.
/// </summary>
public class RuleGroup : IRule
{
    public LogicOperator Logic { get; set; }
    public List<IRule> Rules { get; set; }

    // ── IRule ──
    public string? Name { get; set; }
    public string? Description { get; set; }
    public RuleSeverity Severity { get; set; } = RuleSeverity.Info;
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Negate the entire group result (NOT operator).
    /// </summary>
    public bool IsNegated { get; set; }

    public RuleGroup(LogicOperator logic, List<IRule> rules)
    {
        Logic = logic;
        Rules = rules;
    }

    public RuleGroup(LogicOperator logic, params IRule[] rules)
    {
        Logic = logic;
        Rules = new List<IRule>(rules);
    }

    public static RuleGroup And(params IRule[] rules) => new(LogicOperator.And, rules);
    public static RuleGroup Or(params IRule[] rules) => new(LogicOperator.Or, rules);
}
