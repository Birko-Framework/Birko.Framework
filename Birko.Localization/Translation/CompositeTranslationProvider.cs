using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Chains multiple translation providers by priority. First non-null result wins.
/// </summary>
public sealed class CompositeTranslationProvider : ITranslationProvider
{
    private readonly IReadOnlyList<ITranslationProvider> _providers;

    public CompositeTranslationProvider(params ITranslationProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Length == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }
        _providers = providers.ToList().AsReadOnly();
    }

    public string? GetTranslation(string key, CultureInfo culture)
    {
        foreach (var provider in _providers)
        {
            var result = provider.GetTranslation(key, culture);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    public IReadOnlyList<CultureInfo> GetSupportedCultures()
    {
        return _providers
            .SelectMany(p => p.GetSupportedCultures())
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Iterate in reverse so earlier (higher-priority) providers overwrite later ones
        for (var i = _providers.Count - 1; i >= 0; i--)
        {
            foreach (var (key, value) in _providers[i].GetAll(culture))
            {
                merged[key] = value;
            }
        }

        return merged;
    }
}
