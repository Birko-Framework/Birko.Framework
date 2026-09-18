namespace Birko.Validation.Rules;

/// <summary>
/// Validates that a string value's length falls within a specified range.
/// </summary>
public class LengthRule : IValidationRule
{
    private readonly int? _minLength;
    private readonly int? _maxLength;

    public string PropertyName { get; }
    public string ErrorCode => "INVALID_LENGTH";
    public string ErrorMessage { get; }

    public LengthRule(string propertyName, int? minLength = null, int? maxLength = null, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _minLength = minLength;
        _maxLength = maxLength;
        ErrorMessage = errorMessage ?? FormatDefaultMessage(propertyName, minLength, maxLength);
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        if (value is null) return true;
        if (value is not string s) return false;

        if (_minLength.HasValue && s.Length < _minLength.Value)
            return false;

        if (_maxLength.HasValue && s.Length > _maxLength.Value)
            return false;

        return true;
    }

    private static string FormatDefaultMessage(string propertyName, int? min, int? max)
    {
        if (min.HasValue && max.HasValue)
            return $"'{propertyName}' must be between {min} and {max} characters.";
        if (min.HasValue)
            return $"'{propertyName}' must be at least {min} characters.";
        return $"'{propertyName}' must be at most {max} characters.";
    }
}
