using System.Collections.Concurrent;
using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// In-memory translation provider backed by a dictionary. Useful for testing.
/// </summary>
public sealed class InMemoryTranslationProvider : ITranslationProvider
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translations;

    /// <summary>
    /// Creates a provider from a pre-built dictionary. Key is culture name, value is key-value translations.
    /// </summary>
    public InMemoryTranslationProvider(IDictionary<string, IDictionary<string, string>> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        _translations = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cultureName, entries) in translations)
        {
            var dict = new ConcurrentDictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
            _translations[cultureName] = dict;
        }
    }

    private InMemoryTranslationProvider()
    {
        _translations = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }

    public string? GetTranslation(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (_translations.TryGetValue(culture.Name, out var dict) && dict.TryGetValue(key, out var value))
        {
            return value;
        }
        return null;
    }

    public IReadOnlyList<CultureInfo> GetSupportedCultures()
    {
        return _translations.Keys
            .Select(name => string.IsNullOrEmpty(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name))
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (_translations.TryGetValue(culture.Name, out var dict))
        {
            return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        return new Dictionary<string, string>();
    }

    /// <summary>Creates a builder for fluent construction.</summary>
    public static Builder Create() => new();

    public sealed class Builder
    {
        private readonly Dictionary<string, IDictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);

        public Builder AddTranslation(string cultureName, string key, string value)
        {
            if (!_data.TryGetValue(cultureName, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _data[cultureName] = dict;
            }
            dict[key] = value;
            return this;
        }

        public InMemoryTranslationProvider Build() => new(new Dictionary<string, IDictionary<string, string>>(_data));
    }
}
