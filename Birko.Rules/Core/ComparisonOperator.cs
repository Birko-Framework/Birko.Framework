namespace Birko.Rules;

/// <summary>
/// Comparison operators for rule evaluation.
/// Superset of Birko.Data.SQL.ConditionType — SQL layer can map from these.
/// </summary>
public enum ComparisonOperator
{
    // Equality
    Equal,
    NotEqual,

    // Numeric/comparable
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,

    // Null checks
    IsNull,
    IsNotNull,

    // String
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    Like,

    // Collection
    In,
    NotIn
}
