using System.Globalization;

namespace Birko.Localization;

/// <summary>
/// Immutable configuration for the localization system.
/// </summary>
public sealed class LocalizationSettings
{
    /// <summary>Default culture used as final fallback.</summary>
    public CultureInfo DefaultCulture { get; }

    /// <summary>Whether to try parent culture before default (e.g., sk-SK → sk → default).</summary>
    public bool FallbackToParentCulture { get; }

    /// <summary>Behavior when a translation key is not found after all fallbacks.</summary>
    public MissingKeyBehavior MissingKeyBehavior { get; }

    /// <summary>Optional prefix prepended to all keys before lookup.</summary>
    public string? KeyPrefix { get; }

    private LocalizationSettings(
        CultureInfo defaultCulture,
        bool fallbackToParentCulture,
        MissingKeyBehavior missingKeyBehavior,
        string? keyPrefix)
    {
        DefaultCulture = defaultCulture ?? throw new ArgumentNullException(nameof(defaultCulture));
        FallbackToParentCulture = fallbackToParentCulture;
        MissingKeyBehavior = missingKeyBehavior;
        KeyPrefix = keyPrefix;
    }

    /// <summary>Default settings: InvariantCulture, parent fallback enabled, return key on miss.</summary>
    public static LocalizationSettings Default { get; } = new(
        CultureInfo.InvariantCulture,
        fallbackToParentCulture: true,
        missingKeyBehavior: MissingKeyBehavior.ReturnKey,
        keyPrefix: null);

    public LocalizationSettings WithDefaultCulture(CultureInfo culture)
        => new(culture, FallbackToParentCulture, MissingKeyBehavior, KeyPrefix);

    public LocalizationSettings WithFallbackToParentCulture(bool enabled)
        => new(DefaultCulture, enabled, MissingKeyBehavior, KeyPrefix);

    public LocalizationSettings WithMissingKeyBehavior(MissingKeyBehavior behavior)
        => new(DefaultCulture, FallbackToParentCulture, behavior, KeyPrefix);

    public LocalizationSettings WithKeyPrefix(string? prefix)
        => new(DefaultCulture, FallbackToParentCulture, MissingKeyBehavior, prefix);
}
