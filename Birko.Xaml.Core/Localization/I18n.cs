using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Birko.Xaml.Core.Localization;

/// <summary>
/// Default <see cref="II18n"/> implementation: a dictionary of locales, each a key→string map.
/// Avalonia-free (WPF-addendum constraint #1). A process-wide <see cref="Instance"/> mirrors the
/// Birko.Web singleton; apps may also construct their own instance.
/// </summary>
public sealed class I18n : II18n
{
    /// <summary>Shared process-wide instance (what the <c>{l:Tr}</c> markup extension binds to by default).</summary>
    public static I18n Instance { get; } = new();

    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _locales =
        new(StringComparer.OrdinalIgnoreCase);

    public string Locale { get; private set; } = "en";

    /// <summary>Optional fallback locale consulted when a key is missing in the active locale.</summary>
    public string? FallbackLocale { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LocaleChanged;

    /// <summary>Register (or replace) a locale's translations.</summary>
    public I18n AddLocale(string locale, IReadOnlyDictionary<string, string> translations)
    {
        _locales[locale] = translations;
        // A binding may already resolve against this locale — refresh.
        if (string.Equals(locale, Locale, StringComparison.OrdinalIgnoreCase))
            RaiseIndexer();
        return this;
    }

    public string this[string key]
    {
        get
        {
            if (_locales.TryGetValue(Locale, out var map) && map.TryGetValue(key, out var value))
                return value;
            if (FallbackLocale is not null
                && _locales.TryGetValue(FallbackLocale, out var fb)
                && fb.TryGetValue(key, out var fbValue))
                return fbValue;
            return key; // last resort: show the key so a missing translation is visible, not blank
        }
    }

    public string Translate(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        string template = this[key];
        if (args is null || args.Count == 0) return template;
        return Placeholder.Replace(template, m =>
            args.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? string.Empty : m.Value);
    }

    public void SetLocale(string locale)
    {
        if (string.Equals(locale, Locale, StringComparison.OrdinalIgnoreCase)) return;
        Locale = locale;
        RaiseIndexer();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Locale)));
        LocaleChanged?.Invoke(this, EventArgs.Empty);
    }

    // "Item[]" is the .NET convention for "all indexer values changed" (WPF honors it directly).
    // Avalonia doesn't observe INPC on indexer accessors, so its {l:Tr} binds a real property that
    // refreshes on LocaleChanged instead — see Birko.Xaml.Avalonia's TrExtension.
    private void RaiseIndexer() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
