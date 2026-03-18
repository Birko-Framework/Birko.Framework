using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Culture-aware date and time formatting.
/// </summary>
public interface IDateFormatter
{
    /// <summary>Formats a date using the culture's short date pattern.</summary>
    string Format(DateTime value, CultureInfo? culture = null);

    /// <summary>Formats a date using a custom format string.</summary>
    string Format(DateTime value, string format, CultureInfo? culture = null);

    /// <summary>
    /// Formats a date as a relative time string (e.g., "2 days ago", "in 3 hours").
    /// </summary>
    string FormatRelative(DateTime value, DateTime relativeTo, CultureInfo? culture = null);
}
