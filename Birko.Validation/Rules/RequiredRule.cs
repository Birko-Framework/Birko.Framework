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
            IEnumerable e => HasAnyElement(e),
            Guid g => g != Guid.Empty,
            _ => true
        };
    }

    // CR-L388: for a lazy / DB-backed IEnumerable (not an ICollection), the enumerator can hold resources and
    // is usually IDisposable, so dispose it after probing MoveNext instead of leaking it. The non-generic
    // IEnumerator itself is not IDisposable, hence the cast-and-dispose in the finally.
    private static bool HasAnyElement(IEnumerable source)
    {
        var enumerator = source.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
