using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Culture-aware date formatting with relative time support.
/// </summary>
public sealed class DateFormatter : IDateFormatter
{
    private readonly ICultureResolver _cultureResolver;

    public DateFormatter(ICultureResolver cultureResolver)
    {
        _cultureResolver = cultureResolver ?? throw new ArgumentNullException(nameof(cultureResolver));
    }

    public DateFormatter() : this(new ThreadCultureResolver()) { }

    public string Format(DateTime value, CultureInfo? culture = null)
    {
        return value.ToString("d", ResolveCulture(culture));
    }

    public string Format(DateTime value, string format, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return value.ToString(format, ResolveCulture(culture));
    }

    /// <summary>
    /// Returns a relative-time phrase (e.g. "just now", "3 minutes ago", "yesterday").
    /// </summary>
    /// <remarks>
    /// CR-L275: the relative phrasing is <b>English-only</b> by design — the <paramref name="culture"/>
    /// parameter is accepted for signature symmetry with the other formatters but does not affect the
    /// wording here (it governs numeric/date formatting in <see cref="Format(DateTime, CultureInfo?)"/> and
    /// the format-string overload). Localized relative phrasing (via a pluralizer-aware key set) is out of
    /// scope for this version; callers needing translated relative time should build it from ILocalizer.
    /// </remarks>
    public string FormatRelative(DateTime value, DateTime relativeTo, CultureInfo? culture = null)
    {
        var diff = relativeTo - value;
        var absDiff = diff.Duration();

        if (absDiff.TotalSeconds < 60)
        {
            return diff >= TimeSpan.Zero ? "just now" : "in a moment";
        }

        if (absDiff.TotalMinutes < 60)
        {
            var minutes = (int)absDiff.TotalMinutes;
            return diff >= TimeSpan.Zero
                ? $"{minutes} minute{(minutes == 1 ? "" : "s")} ago"
                : $"in {minutes} minute{(minutes == 1 ? "" : "s")}";
        }

        if (absDiff.TotalHours < 24)
        {
            var hours = (int)absDiff.TotalHours;
            return diff >= TimeSpan.Zero
                ? $"{hours} hour{(hours == 1 ? "" : "s")} ago"
                : $"in {hours} hour{(hours == 1 ? "" : "s")}";
        }

        if (absDiff.TotalDays < 2)
        {
            return diff >= TimeSpan.Zero ? "yesterday" : "tomorrow";
        }

        if (absDiff.TotalDays < 30)
        {
            var days = (int)absDiff.TotalDays;
            return diff >= TimeSpan.Zero
                ? $"{days} days ago"
                : $"in {days} days";
        }

        if (absDiff.TotalDays < 365)
        {
            var months = (int)(absDiff.TotalDays / 30);
            return diff >= TimeSpan.Zero
                ? $"{months} month{(months == 1 ? "" : "s")} ago"
                : $"in {months} month{(months == 1 ? "" : "s")}";
        }

        var years = (int)(absDiff.TotalDays / 365);
        return diff >= TimeSpan.Zero
            ? $"{years} year{(years == 1 ? "" : "s")} ago"
            : $"in {years} year{(years == 1 ? "" : "s")}";
    }

    private CultureInfo ResolveCulture(CultureInfo? culture)
    {
        return culture ?? _cultureResolver.GetCurrentCulture();
    }
}
