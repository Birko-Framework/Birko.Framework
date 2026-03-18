using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Backend contract for loading translations from a storage medium.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>
    /// Gets a single translation. Returns null if not found.
    /// </summary>
    string? GetTranslation(string key, CultureInfo culture);

    /// <summary>
    /// Returns all cultures that have at least one translation.
    /// </summary>
    IReadOnlyList<CultureInfo> GetSupportedCultures();

    /// <summary>
    /// Returns all key-value pairs for the given culture.
    /// </summary>
    IReadOnlyDictionary<string, string> GetAll(CultureInfo culture);
}
