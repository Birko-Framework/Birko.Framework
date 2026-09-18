namespace Birko.Rules;

/// <summary>
/// Leaf rule — single field condition: "field operator value".
/// Aligned with Birko.Data.SQL.Condition structure but DB-independent.
/// </summary>
public class Rule : IRule
{
    public string Field { get; set; }
    public ComparisonOperator Operator { get; set; }
    public object? Value { get; set; }

    /// <summary>
    /// Second value for Between operator (inclusive range: Value &lt;= field &lt;= UpperValue).
    /// </summary>
    public object? UpperValue { get; set; }

    /// <summary>
    /// Negate the result (NOT operator).
    /// </summary>
    public bool IsNegated { get; set; }

    // ── IRule ──
    public string? Name { get; set; }
    public string? Description { get; set; }
    public RuleSeverity Severity { get; set; } = RuleSeverity.Info;
    public bool IsEnabled { get; set; } = true;

    public Rule(string field, ComparisonOperator op, object? value)
    {
        Field = field;
        Operator = op;
        Value = value;
    }

    /// <summary>
    /// Creates a Between rule (inclusive).
    /// </summary>
    public static Rule Between(string field, object lower, object upper) => new(field, ComparisonOperator.Between, lower)
    {
        UpperValue = upper
    };
}
