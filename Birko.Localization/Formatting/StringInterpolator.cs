using System;
using System.Globalization;
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
    /// <param name="formatProvider">
    /// CR-L276: culture used to format <see cref="IFormattable"/> values (decimals, dates) so the result
    /// matches the resolved translation culture rather than the ambient thread culture. Defaults to
    /// <see cref="CultureInfo.CurrentCulture"/> when not supplied.
    /// </param>
    public static string Interpolate(string template, IDictionary<string, object?> args, IFormatProvider? formatProvider = null)
    {
        if (string.IsNullOrEmpty(template) || args == null || args.Count == 0)
        {
            return template;
        }

        var provider = formatProvider ?? CultureInfo.CurrentCulture;
        return NamedPlaceholderRegex.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return args.TryGetValue(name, out var value) ? FormatValue(value, provider) : match.Value;
        });
    }

    /// <summary>
    /// Replaces positional placeholders like {0}, {1} using string.Format.
    /// </summary>
    /// <param name="formatProvider">
    /// CR-L276: culture passed to <see cref="string.Format(IFormatProvider, string, object?[])"/> so
    /// numeric/date args format in the resolved translation culture, not the ambient thread culture.
    /// Defaults to <see cref="CultureInfo.CurrentCulture"/> when not supplied.
    /// </param>
    public static string Interpolate(string template, object[] args, IFormatProvider? formatProvider = null)
    {
        if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
        {
            return template;
        }

        return string.Format(formatProvider ?? CultureInfo.CurrentCulture, template, args);
    }

    private static string FormatValue(object? value, IFormatProvider provider)
        => value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, provider),
            _ => value.ToString() ?? string.Empty
        };
}
