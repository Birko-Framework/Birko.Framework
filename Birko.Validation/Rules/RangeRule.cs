using System;

using System.Globalization;

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

        if (_min is not null && Compare(comparable, _min) < 0)
            return false;
        if (_max is not null && Compare(comparable, _max) > 0)
            return false;

        return true;
    }

    /// <summary>
    /// Compares a value with a bound of another numeric type by value. SH-M371 (TASK-156): <c>Range(-50, 150)</c> on a
    /// <c>double</c> property called <c>Double.CompareTo(object)</c> with a boxed <c>int</c>, which throws "Object must be
    /// of type Double" out of Validate instead of reporting OUT_OF_RANGE. Two numbers compare exactly (decimal) unless one
    /// is a double or float. A bound that is not comparable with the value at all is a misconfigured rule, and says so.
    /// </summary>
    private int Compare(IComparable value, IComparable bound)
    {
        if (value.GetType() == bound.GetType())
            return value.CompareTo(bound);
        if (IsNumber(value) && IsNumber(bound))
        {
            if (value is double or float || bound is double or float)
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(bound, CultureInfo.InvariantCulture));
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(bound, CultureInfo.InvariantCulture));
        }
        try
        {
            return value.CompareTo(bound);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"The range of '{PropertyName}' has a {bound.GetType().Name} bound, which cannot be compared with its {value.GetType().Name} value.", ex);
        }
    }

    private static bool IsNumber(object value) => value is sbyte or byte or short or ushort or int or uint or long or ulong
        or float or double or decimal;

    private static string FormatDefaultMessage(string propertyName, IComparable? min, IComparable? max)
    {
        if (min is not null && max is not null)
            return $"'{propertyName}' must be between {min} and {max}.";
        if (min is not null)
            return $"'{propertyName}' must be at least {min}.";
        return $"'{propertyName}' must be at most {max}.";
    }
}
