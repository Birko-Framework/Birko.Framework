using System.Text.RegularExpressions;

namespace Birko.Validation.Rules;

/// <summary>
/// Validates that a string value is a valid email address format.
/// </summary>
public partial class EmailRule : IValidationRule
{
    private static readonly Regex EmailRegex = CreateEmailRegex();

    public string PropertyName { get; }
    public string ErrorCode => "INVALID_EMAIL";
    public string ErrorMessage { get; }

    public EmailRule(string propertyName, string? errorMessage = null)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage ?? $"'{propertyName}' is not a valid email address.";
    }

    public bool IsValid(object? value, ValidationContext context)
    {
        if (value is null) return true; // Use RequiredRule for null checks
        if (value is not string s) return false;
        if (string.IsNullOrWhiteSpace(s)) return true; // Use RequiredRule for empty checks
        return EmailRegex.IsMatch(s);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CreateEmailRegex();
}
