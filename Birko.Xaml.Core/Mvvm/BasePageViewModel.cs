using CommunityToolkit.Mvvm.ComponentModel;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Core.Mvvm;

/// <summary>
/// Base for all page ViewModels: busy/loaded state, a localized title, and live re-localization.
/// Built on CommunityToolkit.Mvvm (platform-neutral — works on Avalonia and WPF).
/// </summary>
public abstract partial class BasePageViewModel : ObservableObject, IDisposable
{
    protected BasePageViewModel(II18n? i18n = null)
    {
        I18n = i18n ?? Localization.I18n.Instance;
        I18n.LocaleChanged += OnLocaleChanged;
    }

    /// <summary>The localization service this VM resolves strings through.</summary>
    public II18n I18n { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _title;

    /// <summary>Resolve a localization key in the active locale.</summary>
    public string L(string key) => I18n[key];

    /// <summary>Load the page's data. Override in derived VMs; base just marks loaded.</summary>
    public virtual Task LoadAsync(CancellationToken ct = default)
    {
        IsLoaded = true;
        return Task.CompletedTask;
    }

    /// <summary>On locale change, re-raise all properties so localized bindings/derived values refresh.</summary>
    protected virtual void OnLocaleChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        I18n.LocaleChanged -= OnLocaleChanged;
        GC.SuppressFinalize(this);
    }
}
