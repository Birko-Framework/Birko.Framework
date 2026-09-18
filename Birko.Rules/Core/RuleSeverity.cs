namespace Birko.Rules;

/// <summary>
/// Severity level attached to a rule. Domain-agnostic — consumers interpret meaning.
/// </summary>
public enum RuleSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}
