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

        // The resolved culture drives the number layout (separators, symbol placement),
        // but the SYMBOL must reflect the requested currency code — otherwise the code
        // parameter is dead and e.g. FormatCurrency(x, "USD", de-DE) wrongly renders €.
        var nfi = (NumberFormatInfo)resolved.NumberFormat.Clone();
        nfi.CurrencySymbol = CurrencySymbols.Value.TryGetValue(currencyCode, out var symbol)
            ? symbol
            : currencyCode.ToUpperInvariant();
        return value.ToString("C", nfi);
    }

    public string FormatPercent(decimal value, CultureInfo? culture = null)
    {
        return value.ToString("P", ResolveCulture(culture));
    }

    private CultureInfo ResolveCulture(CultureInfo? culture)
    {
        return culture ?? _cultureResolver.GetCurrentCulture();
    }

    /// <summary>
    /// ISO 4217 currency code → symbol, derived once from the installed cultures'
    /// <see cref="RegionInfo"/> data. Unknown codes fall back to the code itself.
    /// </summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CurrencySymbols =
        new(BuildCurrencySymbolMap);

    private static IReadOnlyDictionary<string, string> BuildCurrencySymbolMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ci in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(ci.Name);
                var code = region.ISOCurrencySymbol;
                var symbol = region.CurrencySymbol;
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(symbol))
                {
                    continue;
                }

                // A currency is shared across many cultures with varying symbols
                // (e.g. USD → "$" vs "US$"). Prefer the shortest, then ordinal-least,
                // so the chosen symbol is the cleanest glyph and fully deterministic.
                if (!map.TryGetValue(code, out var existing)
                    || symbol.Length < existing.Length
                    || (symbol.Length == existing.Length && string.CompareOrdinal(symbol, existing) < 0))
                {
                    map[code] = symbol;
                }
            }
            catch (ArgumentException)
            {
                // Culture has no associated region (neutral/custom); skip it.
            }
        }

        return map;
    }
}
