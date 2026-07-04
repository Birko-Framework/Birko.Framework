using Birko.Xaml.Core.Localization;
using Birko.Xaml.Core.Navigation;
using Birko.Xaml.Core.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// The app-shell ViewModel: the navigation service (module list + active page), the theme manager,
/// and a title. Platform-neutral — the Avalonia <c>ShellView</c> (and a future WPF one) binds to it.
/// </summary>
public partial class ShellViewModel : BasePageViewModel
{
    public ShellViewModel(INavigationService navigation, IThemeManager? themes = null, II18n? i18n = null)
        : base(i18n)
    {
        Nav = navigation;
        Themes = themes;
        Nav.Navigated += (_, _) => BackCommand.NotifyCanExecuteChanged();
    }

    public INavigationService Nav { get; }

    /// <summary>Theme switcher (null for a single-theme app — the view hides the switcher).</summary>
    public IThemeManager? Themes { get; }

    [RelayCommand]
    private void Navigate(string moduleId) => Nav.Navigate(moduleId);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Nav.Back();

    private bool CanGoBack() => Nav.CanGoBack;

    [RelayCommand]
    private void SetTheme(string id) => Themes?.SetTheme(id);
}
