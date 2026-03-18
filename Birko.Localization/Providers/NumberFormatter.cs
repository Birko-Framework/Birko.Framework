using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Culture-aware number formatting using .NET CultureInfo.
/// </summary>
public sealed class NumberFormatter : INumberFormatter
{
    private readonly ICultureResolver _cultureResolver;

    public NumberFormatter(ICultureResolver cultureResolver)
    {
        _cultureResolver = cultureResolver ?? throw new ArgumentNullException(nameof(cultureResolver));
    }

    public NumberFormatter() : this(new ThreadCultureResolver()) { }

    public string Format(decimal value, CultureInfo? culture = null)
    {
        return value.ToString("N", ResolveCulture(culture));
    }

    public string FormatCurrency(decimal value, string currencyCode, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        var resolved = ResolveCulture(culture);
        return value.ToString("C", resolved);
    }

    public string FormatPercent(decimal value, CultureInfo? culture = null)
    {
        return value.ToString("P", ResolveCulture(culture));
    }

    private CultureInfo ResolveCulture(CultureInfo? culture)
    {
        return culture ?? _cultureResolver.GetCurrentCulture();
    }
}
