using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace Birko.Messaging.Templates;

/// <summary>
/// <c>{{TotalAmount|money:CurrencyCode}}</c> — an amount in the currency the argument names, written the way the render
/// culture writes money (TASK-815). Czech <c>390,00 Kč</c> / <c>390,00 €</c>; English <c>CZK 390.00</c> / <c>€390.00</c>.
/// </summary>
/// <remarks>
/// <para><b>The argument's currency, never the culture's.</b> A Czech e-mail for a EUR order says EUR. In the culture's
/// own currency the culture's own format is used; any other currency is written with its sign where the browser's
/// <c>Intl.NumberFormat</c> writes one outside its region (<c>€ $ £</c>), else as its ISO code, followed by a no-break
/// space when the culture puts the symbol first. For CZK and EUR in cs and en that equals <c>Intl.NumberFormat</c>, the
/// format a storefront shows (FlowerFurStudio's <c>StorefrontMoney</c>, from which this is generalized).</para>
/// <para><b>The currency's places, whatever the value's scale.</b> <c>390</c>, <c>390.0</c> and <c>390.000</c> all print
/// <c>390,00</c>: a <c>decimal</c> read back from storage keeps the scale it was written with, so <c>ToString()</c>
/// printed <c>390.0</c> next to <c>390</c> in one e-mail.</para>
/// <para>A neutral culture (<c>cs</c>, <c>en</c>) is read as its default specific culture (<c>cs-CZ</c>, <c>en-US</c>),
/// which is what tells it its own currency. Spaces are no-break, so the symbol never wraps onto its own line.</para>
/// </remarks>
public sealed class MoneyTemplateFilter : ITemplateValueFilter
{
    /// <summary>The name templates use: <c>{{Amount|money:CurrencyCode}}</c>.</summary>
    public const string Name = "money";

    private const char NoBreakSpace = ' ';

    private static readonly ConcurrentDictionary<(string Culture, string Currency), NumberFormatInfo> Formats = new();

    public string Apply(object? value, object? argument, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (value is null)
        {
            return string.Empty;
        }

        var currency = (argument as string)?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency))
        {
            throw new TemplateRenderException("inline", "The 'money' filter needs a currency code: {{Amount|money:CurrencyCode}}.");
        }

        decimal amount;
        try
        {
            amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new TemplateRenderException("inline", $"The 'money' filter cannot format a {value.GetType().Name}.", ex);
        }

        var format = Formats.GetOrAdd((culture.Name, currency), key => FormatFor(culture, key.Currency));
        return amount.ToString("C", format).Replace(' ', NoBreakSpace);
    }

    private static NumberFormatInfo FormatFor(CultureInfo culture, string currency)
    {
        var specific = Specific(culture);
        var own = (specific ?? culture).NumberFormat;
        if (specific is not null && string.Equals(RegionOf(specific)?.ISOCurrencySymbol, currency, StringComparison.OrdinalIgnoreCase))
        {
            return own;
        }

        var format = (NumberFormatInfo)own.Clone();
        format.CurrencySymbol = SignOf(currency) ?? (SymbolFirst(own) ? currency + NoBreakSpace : currency);
        format.CurrencyDecimalDigits = DigitsOf(currency) ?? own.CurrencyDecimalDigits;
        return NumberFormatInfo.ReadOnly(format);
    }

    /// <summary>The culture itself when specific, its default specific culture when neutral, null for the invariant culture.</summary>
    private static CultureInfo? Specific(CultureInfo culture)
    {
        if (culture.Name.Length == 0)
        {
            return null;
        }
        if (!culture.IsNeutralCulture)
        {
            return culture;
        }
        try { return CultureInfo.CreateSpecificCulture(culture.Name); }
        catch (CultureNotFoundException) { return null; }
    }

    private static RegionInfo? RegionOf(CultureInfo specific)
    {
        try { return new RegionInfo(specific.Name); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>A currency <c>Intl.NumberFormat</c> writes with a sign outside its own region; any other it writes as its code.</summary>
    private static string? SignOf(string currency) => currency switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        _ => null,
    };

    /// <summary>Whether the culture writes the symbol before the amount (<c>$n</c>, <c>$ n</c>), so a code needs a space after it.</summary>
    private static bool SymbolFirst(NumberFormatInfo format) => format.CurrencyPositivePattern is 0 or 2;

    /// <summary>The currency's own minor-unit places, from a culture whose region uses it (JPY 0, CZK and EUR 2).</summary>
    private static int? DigitsOf(string currency)
    {
        foreach (var candidate in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            if (string.Equals(RegionOf(candidate)?.ISOCurrencySymbol, currency, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.NumberFormat.CurrencyDecimalDigits;
            }
        }
        return null;
    }
}
