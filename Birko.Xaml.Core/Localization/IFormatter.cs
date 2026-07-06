namespace Birko.Xaml.Core.Localization;

/// <summary>Date rendering style for <see cref="IFormatter.Date"/>.</summary>
public enum DateStyle
{
    /// <summary>Culture short date (e.g. en-US <c>1/5/2026</c>).</summary>
    Short,
    /// <summary>Long date without the weekday (e.g. <c>5 January 2026</c>).</summary>
    Long,
    /// <summary>Full long date including the weekday (e.g. <c>Monday, 5 January 2026</c>).</summary>
    Full,
}

/// <summary>
/// Locale-aware value formatting — the XAML analogue of Birko.Web.Core's <c>createFormatter</c>.
/// Bound to an <see cref="II18n"/>: every method resolves the culture from the i18n's active
/// <see cref="II18n.Locale"/> at call time, so switching locale reflows formatting with no re-wiring.
/// <para><see cref="Duration"/> is locale-independent (a stopwatch reading) and matches the web
/// output byte-for-byte; the culture-aware methods map onto .NET <see cref="System.Globalization.CultureInfo"/>.</para>
/// </summary>
public interface IFormatter
{
    /// <summary>Format a date in the active culture. <paramref name="style"/> selects short / long / full.</summary>
    string Date(DateTime value, DateStyle style = DateStyle.Short);

    /// <summary>Format a time of day in the active culture; include seconds when <paramref name="seconds"/> is set.</summary>
    string Time(DateTime value, bool seconds = false);

    /// <summary>Short date + time in the active culture.</summary>
    string DateTime(DateTime value);

    /// <summary>
    /// Elapsed duration from a number of seconds → <c>m:ss</c> (or <c>h:mm:ss</c> when ≥ 1 hour, or
    /// when <paramref name="alwaysHours"/> is set). Negative input clamps to <c>0:00</c>; fractional
    /// seconds are floored. Locale-independent — matches Birko.Web's <c>duration()</c> exactly.
    /// </summary>
    string Duration(double totalSeconds, bool alwaysHours = false);

    /// <summary>Format a number with the active culture's group/decimal separators.
    /// When <paramref name="decimals"/> is null the fractional part is shown to at most 3 digits.</summary>
    string Number(double value, int? decimals = null);

    /// <summary>Format a currency amount: the active culture's grouping/decimals with the
    /// <paramref name="currency"/> code's symbol (independent of culture, mirroring the web).</summary>
    string Currency(double value, string currency = "EUR");

    /// <summary>Format a percentage. Input is 0–100 (e.g. <c>Percent(85)</c> → <c>85%</c>).</summary>
    string Percent(double value, int decimals = 0);
}
