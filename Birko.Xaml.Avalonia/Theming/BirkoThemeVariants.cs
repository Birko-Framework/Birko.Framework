using Avalonia.Styling;
using Birko.Xaml.Core.Theming;

namespace Birko.Xaml.Avalonia.Theming;

/// <summary>
/// The Avalonia <see cref="ThemeVariant"/> instances for the Birko themes. Light/Dark reuse the
/// built-ins; Neon/Finstat are custom variants whose <c>InheritVariant</c> gives their built-in
/// fallback (dark base / light base). These exact instances are used BOTH as the keys in the
/// generated <c>Tokens.axaml</c> <c>ThemeDictionaries</c> (via <c>{x:Static}</c>) AND as the value
/// assigned to <c>RequestedThemeVariant</c>, so lookup matches by identity — no string-key ambiguity.
/// </summary>
public static class BirkoThemeVariants
{
    public static ThemeVariant Light => ThemeVariant.Light;
    public static ThemeVariant Dark => ThemeVariant.Dark;

    public static readonly ThemeVariant Neon = new(BirkoThemes.Neon, ThemeVariant.Dark);
    public static readonly ThemeVariant Finstat = new(BirkoThemes.Finstat, ThemeVariant.Light);

    /// <summary>Map a Birko theme id to its Avalonia variant.</summary>
    public static ThemeVariant ForId(string id) => id switch
    {
        BirkoThemes.Dark => Dark,
        BirkoThemes.Neon => Neon,
        BirkoThemes.Finstat => Finstat,
        _ => Light,
    };

    /// <summary>Map an Avalonia variant back to a Birko theme id.</summary>
    public static string ToId(ThemeVariant variant)
    {
        if (variant == Neon) return BirkoThemes.Neon;
        if (variant == Finstat) return BirkoThemes.Finstat;
        if (variant == Dark) return BirkoThemes.Dark;
        return BirkoThemes.Light;
    }
}
