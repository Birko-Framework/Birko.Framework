using System.ComponentModel;

namespace Birko.Xaml.Core.Localization;

/// <summary>
/// Platform-neutral localization service — the XAML analogue of Birko.Web.Core's <c>i18n</c>
/// singleton. Implements <see cref="INotifyPropertyChanged"/> and raises the indexer property
/// <c>"Item[]"</c> on locale change, so a XAML binding to the indexer (the <c>{l:Tr}</c> markup
/// extension) re-resolves live without a restart.
/// </summary>
public interface II18n : INotifyPropertyChanged
{
    /// <summary>The active locale (e.g. "en", "sk").</summary>
    string Locale { get; }

    /// <summary>Resolve a key in the active locale; falls back to any registered fallback locale, then the key itself.</summary>
    string this[string key] { get; }

    /// <summary>Resolve a key and interpolate <c>{name}</c> placeholders from <paramref name="args"/>.</summary>
    string Translate(string key, IReadOnlyDictionary<string, object?>? args = null);

    /// <summary>Switch the active locale. No-op if already active. Raises <see cref="INotifyPropertyChanged"/> for
    /// <c>"Item[]"</c> + <c>Locale</c> and <see cref="LocaleChanged"/>.</summary>
    void SetLocale(string locale);

    /// <summary>Raised after the active locale changes.</summary>
    event EventHandler? LocaleChanged;
}
