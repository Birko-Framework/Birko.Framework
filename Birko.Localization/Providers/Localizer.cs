using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Default <see cref="ILocalizer"/> implementation with culture fallback chain.
/// </summary>
public sealed class Localizer : ILocalizer
{
    private readonly ITranslationProvider _provider;
    private readonly ICultureResolver _cultureResolver;
    private readonly LocalizationSettings _settings;

    public Localizer(ITranslationProvider provider, ICultureResolver cultureResolver, LocalizationSettings? settings = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _cultureResolver = cultureResolver ?? throw new ArgumentNullException(nameof(cultureResolver));
        _settings = settings ?? LocalizationSettings.Default;
    }

    public Localizer(ITranslationProvider provider, LocalizationSettings? settings = null)
        : this(provider, new ThreadCultureResolver(), settings)
    {
    }

    public string Get(string key, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var resolvedKey = PrefixKey(key);
        var resolvedCulture = culture ?? _cultureResolver.GetCurrentCulture();

        var result = Resolve(resolvedKey, resolvedCulture);
        if (result != null)
        {
            return result;
        }

        return _settings.MissingKeyBehavior switch
        {
            MissingKeyBehavior.ReturnKey => key,
            MissingKeyBehavior.ReturnEmpty => string.Empty,
            MissingKeyBehavior.ThrowException => throw new KeyNotFoundException($"Translation key '{key}' not found for culture '{resolvedCulture.Name}'."),
            _ => key
        };
    }

    public string Get(string key, IDictionary<string, object?> args, CultureInfo? culture = null)
    {
        var template = Get(key, culture);
        return StringInterpolator.Interpolate(template, args);
    }

    public string Get(string key, object[] args, CultureInfo? culture = null)
    {
        var template = Get(key, culture);
        return StringInterpolator.Interpolate(template, args);
    }

    public bool HasTranslation(string key, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var resolvedKey = PrefixKey(key);
        var resolvedCulture = culture ?? _cultureResolver.GetCurrentCulture();
        return Resolve(resolvedKey, resolvedCulture) != null;
    }

    private string? Resolve(string key, CultureInfo culture)
    {
        // 1. Try exact culture
        var result = _provider.GetTranslation(key, culture);
        if (result != null)
        {
            return result;
        }

        // 2. Try parent culture chain (e.g., sk-SK → sk)
        if (_settings.FallbackToParentCulture)
        {
            var parent = culture.Parent;
            while (parent != null && !string.IsNullOrEmpty(parent.Name))
            {
                result = _provider.GetTranslation(key, parent);
                if (result != null)
                {
                    return result;
                }
                parent = parent.Parent;
            }
        }

        // 3. Try default culture
        if (!culture.Equals(_settings.DefaultCulture))
        {
            result = _provider.GetTranslation(key, _settings.DefaultCulture);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private string PrefixKey(string key)
    {
        return string.IsNullOrEmpty(_settings.KeyPrefix) ? key : $"{_settings.KeyPrefix}.{key}";
    }
}
