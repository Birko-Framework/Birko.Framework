using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// CLDR-based pluralizer supporting common language families.
/// Returns the plural form index for a given count and culture.
/// </summary>
public sealed class CldrPluralizer : IPluralizer
{
    private static readonly Dictionary<string, (Func<int, int> Rule, int Count)> PluralRules = new(StringComparer.OrdinalIgnoreCase)
    {
        // East Asian: 1 form (other only)
        ["zh"] = (PluralOther, 1),
        ["ja"] = (PluralOther, 1),
        ["ko"] = (PluralOther, 1),
        ["vi"] = (PluralOther, 1),
        ["th"] = (PluralOther, 1),
        ["id"] = (PluralOther, 1),
        ["ms"] = (PluralOther, 1),

        // Germanic/Romance: 2 forms (one, other)
        ["en"] = (PluralOneOther, 2),
        ["de"] = (PluralOneOther, 2),
        ["nl"] = (PluralOneOther, 2),
        ["sv"] = (PluralOneOther, 2),
        ["da"] = (PluralOneOther, 2),
        ["no"] = (PluralOneOther, 2),
        ["nb"] = (PluralOneOther, 2),
        ["nn"] = (PluralOneOther, 2),
        ["fi"] = (PluralOneOther, 2),
        ["es"] = (PluralOneOther, 2),
        ["it"] = (PluralOneOther, 2),
        ["pt"] = (PluralOneOther, 2),
        ["el"] = (PluralOneOther, 2),
        ["hu"] = (PluralOneOther, 2),
        ["tr"] = (PluralOneOther, 2),
        ["bg"] = (PluralOneOther, 2),
        ["ca"] = (PluralOneOther, 2),

        // French: 2 forms but 0 is singular
        ["fr"] = (PluralFrench, 2),

        // Czech/Slovak: 3 forms (one, few 2-4, other 5+)
        ["sk"] = (PluralCzechSlovak, 3),
        ["cs"] = (PluralCzechSlovak, 3),

        // Polish: 3 forms with modular rules
        ["pl"] = (PluralPolish, 3),

        // Russian/Ukrainian/Serbian/Croatian/Bosnian: 3 forms
        ["ru"] = (PluralEastSlavic, 3),
        ["uk"] = (PluralEastSlavic, 3),
        ["hr"] = (PluralEastSlavic, 3),
        ["sr"] = (PluralEastSlavic, 3),
        ["bs"] = (PluralEastSlavic, 3),
        ["be"] = (PluralEastSlavic, 3),

        // Romanian: 3 forms
        ["ro"] = (PluralRomanian, 3),

        // Lithuanian: 3 forms
        ["lt"] = (PluralLithuanian, 3),

        // Latvian: 3 forms (zero, one, other)
        ["lv"] = (PluralLatvian, 3),

        // Arabic: 6 forms
        ["ar"] = (PluralArabic, 6),
    };

    public int GetPluralForm(int count, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var n = Math.Abs(count);
        var lang = culture.TwoLetterISOLanguageName;
        return PluralRules.TryGetValue(lang, out var entry) ? entry.Rule(n) : PluralOneOther(n);
    }

    public int GetPluralFormCount(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var lang = culture.TwoLetterISOLanguageName;
        return PluralRules.TryGetValue(lang, out var entry) ? entry.Count : 2;
    }

    // 1 form: everything is "other"
    private static int PluralOther(int n) => 0;

    // 2 forms: one (n==1), other
    private static int PluralOneOther(int n) => n == 1 ? 0 : 1;

    // 2 forms: French style (0 and 1 are singular)
    private static int PluralFrench(int n) => n is 0 or 1 ? 0 : 1;

    // 3 forms: Czech/Slovak — one (1), few (2-4), other (5+)
    private static int PluralCzechSlovak(int n)
    {
        if (n == 1) return 0;
        if (n >= 2 && n <= 4) return 1;
        return 2;
    }

    // 3 forms: Polish
    // one: n==1
    // few: n%10 in 2..4 && n%100 not in 12..14
    // other: everything else
    private static int PluralPolish(int n)
    {
        if (n == 1) return 0;
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 1;
        return 2;
    }

    // 3 forms: Russian/Ukrainian/Croatian/Serbian/Bosnian/Belarusian
    // one: n%10==1 && n%100!=11
    // few: n%10 in 2..4 && n%100 not in 12..14
    // other: everything else
    private static int PluralEastSlavic(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return 0;
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 1;
        return 2;
    }

    // 3 forms: Romanian
    // one: n==1
    // few: n==0 || (n%100 in 1..19)
    // other: everything else
    private static int PluralRomanian(int n)
    {
        if (n == 1) return 0;
        if (n == 0 || (n % 100 >= 1 && n % 100 <= 19)) return 1;
        return 2;
    }

    // 3 forms: Lithuanian
    // one: n%10==1 && n%100!=11
    // few: n%10 in 2..9 && n%100 not in 12..19
    // other: everything else
    private static int PluralLithuanian(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return 0;
        if (mod10 >= 2 && mod10 <= 9 && (mod100 < 12 || mod100 > 19)) return 1;
        return 2;
    }

    // 3 forms: Latvian
    // zero: n==0
    // one: n%10==1 && n%100!=11
    // other: everything else
    private static int PluralLatvian(int n)
    {
        if (n == 0) return 0;
        if (n % 10 == 1 && n % 100 != 11) return 1;
        return 2;
    }

    // 6 forms: Arabic
    // zero: n==0, one: n==1, two: n==2
    // few: n%100 in 3..10, many: n%100 in 11..99, other: everything else
    private static int PluralArabic(int n)
    {
        if (n == 0) return 0;
        if (n == 1) return 1;
        if (n == 2) return 2;
        var mod100 = n % 100;
        if (mod100 >= 3 && mod100 <= 10) return 3;
        if (mod100 >= 11 && mod100 <= 99) return 4;
        return 5;
    }
}
