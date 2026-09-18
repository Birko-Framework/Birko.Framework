using System.Globalization;

namespace Birko.Xaml.Core.Localization;

/// <summary>
/// Default <see cref="IFormatter"/> — resolves the culture from a bound <see cref="II18n"/> at call
/// time. Avalonia-free (WPF-addendum constraint #1): uses only <see cref="System.Globalization"/>.
/// </summary>
public sealed class Formatter : IFormatter
{
    private readonly II18n _i18n;

    /// <summary>Create a formatter bound to <paramref name="i18n"/> (defaults to <see cref="I18n.Instance"/>).</summary>
    public Formatter(II18n? i18n = null) => _i18n = i18n ?? I18n.Instance;

    /// <summary>The culture for the i18n's active locale; invariant culture if the locale isn't a known name.</summary>
    private CultureInfo Culture
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(_i18n.Locale); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }

    public string Date(DateTime value, DateStyle style = DateStyle.Short)
    {
        var culture = Culture;
        return style switch
        {
            DateStyle.Short => value.ToString("d", culture),
            DateStyle.Full => value.ToString("D", culture),
            // "Long" = the culture's full long date minus the weekday token (Intl { day, month:'long', year }).
            _ => value.ToString(LongDatePatternWithoutWeekday(culture), culture),
        };
    }

    public string Time(DateTime value, bool seconds = false)
        => value.ToString(seconds ? "T" : "t", Culture);

    public string DateTime(DateTime value)
        => $"{Date(value)} {Time(value)}";

    public string Duration(double totalSeconds, bool alwaysHours = false)
    {
        // Locale-independent — mirrors Birko.Web.Core fmt.ts duration() exactly.
        long total = (long)Math.Floor(Math.Max(0, totalSeconds));
        long h = total / 3600;
        long m = total % 3600 / 60;
        long s = total % 60;
        return h > 0 || alwaysHours
            ? $"{h}:{m:D2}:{s:D2}"
            : $"{m}:{s:D2}";
    }

    public string Number(double value, int? decimals = null)
        => decimals is int d
            ? value.ToString("N" + d, Culture)
            : value.ToString("#,##0.###", Culture);

    public string Currency(double value, string currency = "EUR")
    {
        // Symbol is driven by the currency CODE (not the culture), matching the web's Intl currency;
        // grouping/decimal separators still come from the active culture.
        var nfi = (NumberFormatInfo)Culture.NumberFormat.Clone();
        nfi.CurrencySymbol = CurrencySymbol(currency);
        return value.ToString("C", nfi);
    }

    public string Percent(double value, int decimals = 0)
        // .NET "P" multiplies by 100, so feed the 0–1 fraction. Web input is 0–100.
        => (value / 100d).ToString("P" + decimals, Culture);

    private static string LongDatePatternWithoutWeekday(CultureInfo culture)
    {
        // Strip the weekday ("dddd") plus any adjacent separator/whitespace from the long date pattern.
        string p = culture.DateTimeFormat.LongDatePattern;
        p = p.Replace("dddd", "").Replace("ddd", "");
        p = p.Replace(", ", " ").Replace(" ,", " ");
        return p.Trim().Trim(',', ' ');
    }

    private static string CurrencySymbol(string currency) => currency?.ToUpperInvariant() switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        "JPY" => "¥",
        "CZK" => "Kč",
        "PLN" => "zł",
        "CHF" => "CHF",
        null or "" => "€",
        _ => currency, // unknown code → show the code itself (e.g. "SEK 1,00")
    };
}
