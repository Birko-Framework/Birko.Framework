using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Main entry point for resolving localized strings.
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// Gets a translated string for the given key.
    /// Falls back through parent cultures and default culture per settings.
    /// </summary>
    string Get(string key, CultureInfo? culture = null);

    /// <summary>
    /// Gets a translated string with named placeholder interpolation.
    /// Placeholders use {name} syntax, e.g., "Hello {userName}".
    /// </summary>
    string Get(string key, IDictionary<string, object?> args, CultureInfo? culture = null);

    /// <summary>
    /// Gets a translated string with positional placeholder interpolation.
    /// Placeholders use {0}, {1} syntax (string.Format).
    /// </summary>
    string Get(string key, object[] args, CultureInfo? culture = null);

    /// <summary>
    /// Checks whether a translation exists for the given key.
    /// </summary>
    bool HasTranslation(string key, CultureInfo? culture = null);
}
