using System.ComponentModel;
using Avalonia.Data;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Avalonia.Markup;

/// <summary>
/// <c>{l:Tr app.title}</c> — resolves a localization key through <see cref="I18n.Instance"/> and
/// re-resolves live when the locale changes. Returns a one-way <see cref="Binding"/> to a small
/// per-binding source whose <c>Value</c> property refreshes on <see cref="II18n.LocaleChanged"/>.
///
/// (Avalonia does not observe <see cref="INotifyPropertyChanged"/> on indexer accessors, so binding
/// straight to <c>I18n[key]</c> wouldn't refresh — hence the tiny <see cref="TrValueSource"/>.)
///
/// Platform-specific by necessity (returns an Avalonia <see cref="Binding"/>); the localization
/// LOGIC lives in the Avalonia-free <c>Birko.Xaml.Core</c>. A WPF skin supplies its own wrapper
/// over the same singleton — see the EPIC-015 WPF addendum.
/// </summary>
public sealed class TrExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    /// <summary>The localization key to resolve.</summary>
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(TrValueSource.Value))
        {
            Source = new TrValueSource(Key),
            Mode = BindingMode.OneWay,
        };
}

/// <summary>Per-binding adapter exposing a localized <see cref="Value"/> that raises change on
/// <see cref="II18n.LocaleChanged"/>. Lives as long as its binding (and the I18n subscription); for
/// long-lived labels this is fine — for churny dynamic UIs prefer resolving in code + manual refresh.</summary>
internal sealed class TrValueSource : INotifyPropertyChanged
{
    private readonly string _key;

    public TrValueSource(string key)
    {
        _key = key;
        I18n.Instance.LocaleChanged += OnLocaleChanged;
    }

    public string Value => I18n.Instance[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLocaleChanged(object? sender, EventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
