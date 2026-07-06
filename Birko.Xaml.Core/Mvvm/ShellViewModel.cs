using Birko.Xaml.Core.Commands;
using Birko.Xaml.Core.Localization;
using Birko.Xaml.Core.Navigation;
using Birko.Xaml.Core.Ribbon;
using Birko.Xaml.Core.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// The app-shell ViewModel: navigation (module list + active page), the theme manager, a title, a
/// command palette (populated from modules + themes), and an optional user area. Platform-neutral —
/// the Avalonia <c>ShellView</c> (and a future WPF one) binds to it.
/// </summary>
public partial class ShellViewModel : BasePageViewModel
{
    public ShellViewModel(INavigationService navigation, IThemeManager? themes = null, II18n? i18n = null)
        : base(i18n)
    {
        Nav = navigation;
        Themes = themes;
        Nav.Navigated += (_, _) => BackCommand.NotifyCanExecuteChanged();
        PaletteCommands = BuildPaletteCommands();
    }

    public INavigationService Nav { get; }

    /// <summary>Theme switcher (null for a single-theme app — the view hides the switcher).</summary>
    public IThemeManager? Themes { get; }

    /// <summary>Commands shown in the Ctrl+K palette — navigate-to-module + switch-theme entries.</summary>
    public IReadOnlyList<CommandItem> PaletteCommands { get; }

    [ObservableProperty]
    private bool _isPaletteOpen;

    /// <summary>Signed-in user's display name. Empty/null hides the header user area.</summary>
    [ObservableProperty]
    private string? _userName;

    /// <summary>Menu entries for the user dropdown (e.g. Profile, Sign out). Empty = static badge.</summary>
    public IReadOnlyList<CommandItem> UserCommands { get; set; } = Array.Empty<CommandItem>();

    /// <summary>Ribbon tabs for the ribbon shell variant (`RibbonShellView` / BAppShell chrome).</summary>
    public IReadOnlyList<RibbonTab> RibbonTabs { get; set; } = Array.Empty<RibbonTab>();

    /// <summary>Tenants the user can switch between. Fewer than 2 hides the header tenant switcher.</summary>
    public IReadOnlyList<string> Tenants { get; set; } = Array.Empty<string>();

    /// <summary>Whether to show the tenant switcher (more than one tenant).</summary>
    public bool HasMultipleTenants => Tenants.Count > 1;

    /// <summary>The active tenant (two-way bound to the switcher).</summary>
    [ObservableProperty]
    private string? _currentTenant;

    [RelayCommand]
    private void Navigate(string moduleId) => Nav.Navigate(moduleId);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Nav.Back();

    private bool CanGoBack() => Nav.CanGoBack;

    [RelayCommand]
    private void SetTheme(string id) => Themes?.SetTheme(id);

    [RelayCommand]
    private void OpenPalette() => IsPaletteOpen = true;

    [RelayCommand]
    private void RunUserCommand(CommandItem? item) => item?.Run?.Invoke();

    private List<CommandItem> BuildPaletteCommands()
    {
        var commands = new List<CommandItem>();
        foreach (var module in Nav.Modules)
        {
            var id = module.Id;
            commands.Add(new CommandItem
            {
                Id = "nav:" + id,
                Label = "Go to " + module.Label,
                Group = "Navigate",
                Run = () => Nav.Navigate(id),
            });
        }
        if (Themes is not null)
        {
            foreach (var theme in Themes.Available)
            {
                var themeId = theme.Id;
                commands.Add(new CommandItem
                {
                    Id = "theme:" + themeId,
                    Label = "Theme: " + theme.Label,
                    Group = "Appearance",
                    Run = () => Themes.SetTheme(themeId),
                });
            }
        }
        return commands;
    }
}
