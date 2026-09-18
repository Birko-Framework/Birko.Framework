using System;

namespace Birko.Validation;

/// <summary>
/// Thrown when validation fails, typically by ValidatingStoreWrapper.
/// </summary>
public class ValidationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public ValidationException(ValidationResult result)
        : base(FormatMessage(result))
    {
        ValidationResult = result;
    }

    private static string FormatMessage(ValidationResult result)
    {
        return $"Validation failed: {result.Errors.Count} error(s). " +
               string.Join("; ", result.Errors);
    }
}
