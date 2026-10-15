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

    /// <summary>
    /// The longest one match may run before the value is reported invalid (TASK-156, SH-M376).
    /// </summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    /// <remarks>
    /// SH-M376 (TASK-156): a pattern can come from configuration, so it runs with a match timeout
    /// (<see cref="DefaultMatchTimeout"/> unless given); a catastrophically backtracking one no longer spins the request
    /// thread. The default options are no longer <c>Compiled</c>: this constructor bypasses the regex cache, so every
    /// validator instance emitted IL that is never reclaimed. Pass <c>RegexOptions.Compiled</c> for a long-lived one.
    /// </remarks>
    public RegexRule(string propertyName, string pattern, RegexOptions options = RegexOptions.None, string? errorMessage = null,
        TimeSpan? matchTimeout = null)
    {
        PropertyName = propertyName;
        _regex = new Regex(pattern, options, matchTimeout ?? DefaultMatchTimeout);
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
        try
        {
            return _regex.IsMatch(s);
        }
        catch (RegexMatchTimeoutException)
        {
            // A value the pattern cannot decide within the timeout is not accepted.
            return false;
        }
    }
}
