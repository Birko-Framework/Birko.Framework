using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Selects the correct plural form index for a given count and culture.
/// </summary>
public interface IPluralizer
{
    /// <summary>
    /// Returns the plural form index (0-based) for the given count.
    /// For example, Slovak has 3 forms: 0 (one), 1 (few: 2-4), 2 (other: 5+).
    /// </summary>
    int GetPluralForm(int count, CultureInfo culture);

    /// <summary>
    /// Returns the number of plural forms for the given culture.
    /// </summary>
    int GetPluralFormCount(CultureInfo culture);
}
