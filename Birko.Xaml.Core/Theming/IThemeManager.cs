namespace Birko.Xaml.Core.Theming;

/// <summary>
/// Platform-neutral theme switcher — the XAML analogue of the Birko.Web shell's
/// <c>setTheme</c> / <c>getRegisteredThemes</c> / <c>currentTheme</c>. The Avalonia skin
/// implements this over <c>RequestedThemeVariant</c>; a future WPF skin implements it over
/// swapped <c>MergedDictionaries</c> — consumers depend only on this interface.
/// </summary>
public interface IThemeManager
{
    /// <summary>The themes this app offers, in switcher order.</summary>
    IReadOnlyList<ThemeInfo> Available { get; }

    /// <summary>The currently active theme.</summary>
    ThemeInfo Current { get; }

    /// <summary>Raised after <see cref="SetTheme"/> changes the active theme.</summary>
    event Action<ThemeInfo>? ThemeChanged;

    /// <summary>Activate a theme by id. Unknown ids are ignored (current theme kept).</summary>
    /// <returns><c>true</c> if the theme was found and applied.</returns>
    bool SetTheme(string id);
}
