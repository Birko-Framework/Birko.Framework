using System;
using System.Collections;

namespace Birko.Validation.Rules;

/// <summary>
/// Validates that a value is not null, not empty string, and not empty collection.
/// </summary>
public class RequiredRule : IValidationRule
{
    public string PropertyName { get; }
    public string ErrorCode => "REQUIRED";
    public string ErrorMessage { get; }

    public RequiredRule(string propertyName, string? errorMessage = null)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage ?? $"'{propertyName}' is required.";
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        return value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            Guid g => g != Guid.Empty,
            _ => true
        };
    }
}
