using System;

namespace Birko.Validation.Rules;

/// <summary>
/// Validates that a comparable value falls within a specified range.
/// </summary>
public class RangeRule : IValidationRule
{
    private readonly IComparable? _min;
    private readonly IComparable? _max;

    public string PropertyName { get; }
    public string ErrorCode => "OUT_OF_RANGE";
    public string ErrorMessage { get; }

    public RangeRule(string propertyName, IComparable? min = null, IComparable? max = null, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _min = min;
        _max = max;
        ErrorMessage = errorMessage ?? FormatDefaultMessage(propertyName, min, max);
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        if (value is null) return true; // Use RequiredRule for null checks
        if (value is not IComparable comparable) return false;

        if (_min is not null && comparable.CompareTo(_min) < 0)
            return false;

        if (_max is not null && comparable.CompareTo(_max) > 0)
            return false;

        return true;
    }

    private static string FormatDefaultMessage(string propertyName, IComparable? min, IComparable? max)
    {
        if (min is not null && max is not null)
            return $"'{propertyName}' must be between {min} and {max}.";
        if (min is not null)
            return $"'{propertyName}' must be at least {min}.";
        return $"'{propertyName}' must be at most {max}.";
    }
}
