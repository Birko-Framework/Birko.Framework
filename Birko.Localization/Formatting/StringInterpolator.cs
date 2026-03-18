using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Localization;

/// <summary>
/// Utility for interpolating named and positional placeholders in strings.
/// </summary>
internal static class StringInterpolator
{
    private static readonly Regex NamedPlaceholderRegex = new(
        @"\{(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces named placeholders like {userName} with values from the dictionary.
    /// Unmatched placeholders are left as-is.
    /// </summary>
    public static string Interpolate(string template, IDictionary<string, object?> args)
    {
        if (string.IsNullOrEmpty(template) || args == null || args.Count == 0)
        {
            return template;
        }

        return NamedPlaceholderRegex.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return args.TryGetValue(name, out var value) ? value?.ToString() ?? string.Empty : match.Value;
        });
    }

    /// <summary>
    /// Replaces positional placeholders like {0}, {1} using string.Format.
    /// </summary>
    public static string Interpolate(string template, object[] args)
    {
        if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
        {
            return template;
        }

        return string.Format(template, args);
    }
}
