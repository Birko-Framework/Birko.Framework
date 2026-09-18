using System;
using System.Linq.Expressions;

namespace Birko.Localization.Data;

/// <summary>
/// Filter for querying translations by culture, key, or namespace.
/// </summary>
public class TranslationFilter
{
    public string? Culture { get; set; }
    public string? Key { get; set; }
    public string? Namespace { get; set; }

    public Expression<Func<TranslationModel, bool>> ToExpression()
    {
        return t =>
            (Culture == null || t.Culture == Culture) &&
            (Key == null || t.Key == Key) &&
            (Namespace == null || t.Namespace == Namespace);
    }

    /// <summary>Creates a filter for all translations of a specific culture.</summary>
    public static TranslationFilter ByCulture(string culture)
        => new() { Culture = culture };

    /// <summary>Creates a filter for a specific key and culture.</summary>
    public static TranslationFilter ByKeyAndCulture(string key, string culture)
        => new() { Key = key, Culture = culture };

    /// <summary>Creates a filter for all translations in a namespace and culture.</summary>
    public static TranslationFilter ByNamespaceAndCulture(string ns, string culture)
        => new() { Namespace = ns, Culture = culture };
}
