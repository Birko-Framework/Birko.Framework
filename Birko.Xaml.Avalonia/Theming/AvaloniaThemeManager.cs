using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Birko.Xaml.Core.Theming;

namespace Birko.Xaml.Avalonia.Theming;

/// <summary>
/// <see cref="IThemeManager"/> over Avalonia's <c>RequestedThemeVariant</c>. Setting the variant
/// makes every <c>{DynamicResource}</c> reference re-resolve against the matching entry in the
/// generated <c>Tokens.axaml</c> <c>ThemeDictionaries</c> — a live, restart-free swap.
/// </summary>
public sealed class AvaloniaThemeManager : IThemeManager
{
    private readonly Application _app;

    /// <summary>The resource key each generated theme dictionary uses to name itself. Mirrors
    /// <c>AxamlEmitter.ThemeIdKey</c> (the generator writes it; this reads it).</summary>
    private const string ThemeIdKey = "BThemeId";

    /// <summary>Create a manager over an explicit application (defaults to <see cref="Application.Current"/>).
    /// <paramref name="available"/> overrides the theme list; when omitted it is <b>detected</b> from
    /// the dictionaries actually merged into the app, so the switcher cannot offer a theme whose
    /// tokens were not shipped.</summary>
    public AvaloniaThemeManager(Application? app = null, IReadOnlyList<ThemeInfo>? available = null)
    {
        _app = app ?? Application.Current
               ?? throw new InvalidOperationException(
                   "No Avalonia Application is running; construct AvaloniaThemeManager after the app is initialized.");
        Available = available ?? DetectThemes(_app);
    }

    /// <summary>
    /// Which Birko themes the given resources actually ship. A theme's <i>presence</i> cannot be
    /// probed directly: an omitted custom variant silently resolves through its
    /// <c>InheritVariant</c> (Neon→Dark), so any key would still be found. Instead each generated
    /// dictionary declares <c>BThemeId</c> naming itself, so the value tells us which dictionary
    /// actually answered — if a Neon lookup replies "dark", Neon was not shipped.
    /// <para>Public so a consumer can ask what its own composition offers (and so this is testable
    /// against a bare <see cref="ResourceDictionary"/> without standing up an Application).</para>
    /// </summary>
    public static IReadOnlyList<ThemeInfo> DetectThemes(IResourceNode resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var found = new List<ThemeInfo>();
        foreach (var info in BirkoThemes.All)
        {
            if (resources.TryGetResource(ThemeIdKey, BirkoThemeVariants.ForId(info.Id), out var id)
                && id as string == info.Id)
                found.Add(info);
        }

        // No Birko tokens merged at all (or a consumer on a hand-rolled dictionary): keep the
        // switcher coherent with light, which is the base theme everywhere.
        return found.Count > 0 ? found : new[] { BirkoThemes.LightTheme };
    }

    public IReadOnlyList<ThemeInfo> Available { get; }

    public ThemeInfo Current
    {
        get
        {
            string id = BirkoThemeVariants.ToId(_app.ActualThemeVariant);
            return BirkoThemes.ById(id) ?? BirkoThemes.LightTheme;
        }
    }

    public event Action<ThemeInfo>? ThemeChanged;

    public bool SetTheme(string id)
    {
        var info = Available.FirstOrDefault(t => t.Id == id);
        if (info is null) return false;

        _app.RequestedThemeVariant = BirkoThemeVariants.ForId(id);
        ThemeChanged?.Invoke(info);
        return true;
    }
}
