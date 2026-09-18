namespace Birko.Rules;

/// <summary>
/// A single evaluation unit — either a leaf condition or a composite group.
/// </summary>
public interface IRule
{
    string? Name { get; }
    string? Description { get; }
    RuleSeverity Severity { get; }
    bool IsEnabled { get; }
}
