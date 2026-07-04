using Avalonia;
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

    /// <summary>Create a manager over an explicit application (defaults to <see cref="Application.Current"/>).</summary>
    public AvaloniaThemeManager(Application? app = null, IReadOnlyList<ThemeInfo>? available = null)
    {
        _app = app ?? Application.Current
               ?? throw new InvalidOperationException(
                   "No Avalonia Application is running; construct AvaloniaThemeManager after the app is initialized.");
        Available = available ?? BirkoThemes.All;
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
