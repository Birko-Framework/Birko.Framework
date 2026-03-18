using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Culture-aware number formatting.
/// </summary>
public interface INumberFormatter
{
    /// <summary>Formats a number using the culture's standard numeric format.</summary>
    string Format(decimal value, CultureInfo? culture = null);

    /// <summary>Formats a value as currency with the specified currency code.</summary>
    string FormatCurrency(decimal value, string currencyCode, CultureInfo? culture = null);

    /// <summary>Formats a value as a percentage.</summary>
    string FormatPercent(decimal value, CultureInfo? culture = null);
}
