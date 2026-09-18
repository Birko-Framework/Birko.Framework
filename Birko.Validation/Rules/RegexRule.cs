using System.Text.RegularExpressions;

namespace Birko.Validation.Rules;

/// <summary>
/// Validates that a string value matches a regular expression pattern.
/// </summary>
public class RegexRule : IValidationRule
{
    private readonly Regex _regex;

    public string PropertyName { get; }
    public string ErrorCode => "INVALID_FORMAT";
    public string ErrorMessage { get; }

    public RegexRule(string propertyName, string pattern, RegexOptions options = RegexOptions.Compiled, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _regex = new Regex(pattern, options);
        ErrorMessage = errorMessage ?? $"'{propertyName}' has an invalid format.";
    }

    public RegexRule(string propertyName, Regex regex, string? errorMessage = null)
    {
        PropertyName = propertyName;
        _regex = regex;
        ErrorMessage = errorMessage ?? $"'{propertyName}' has an invalid format.";
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        if (value is null) return true;
        if (value is not string s) return false;
        if (string.IsNullOrEmpty(s)) return true;
        return _regex.IsMatch(s);
    }
}
