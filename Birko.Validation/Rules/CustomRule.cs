using System;

namespace Birko.Validation.Rules;

/// <summary>
/// Validates a value using a custom predicate function.
/// </summary>
public class CustomRule : IValidationRule
{
    private readonly Func<object?, ValidationContext, bool> _predicate;

    public string PropertyName { get; }
    public string ErrorCode { get; }
    public string ErrorMessage { get; }

    public CustomRule(string propertyName, Func<object?, ValidationContext, bool> predicate, string? errorCode = null, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        ErrorCode = errorCode ?? "CUSTOM_VALIDATION";
        ErrorMessage = errorMessage ?? $"'{propertyName}' is invalid.";
    }

    public bool IsValid(object? value, ValidationContext context) => _predicate(value, context);
}

/// <summary>
/// Strongly-typed custom rule that receives the full model instance.
/// </summary>
public class CustomRule<T> : IValidationRule
{
    private readonly Func<T, bool> _predicate;

    public string PropertyName { get; }
    public string ErrorCode { get; }
    public string ErrorMessage { get; }

    public CustomRule(string propertyName, Func<T, bool> predicate, string? errorCode = null, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        ErrorCode = errorCode ?? "CUSTOM_VALIDATION";
        ErrorMessage = errorMessage ?? $"'{propertyName}' is invalid.";
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        if (context.Instance is T typed)
            return _predicate(typed);
        return true;
    }
}
