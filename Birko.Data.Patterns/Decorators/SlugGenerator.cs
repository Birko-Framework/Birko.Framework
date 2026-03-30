using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Slug normalization and uniqueness utilities.
/// Used by slug store wrappers and available for direct use in services
/// that need the resolved slug before persisting (e.g. hierarchical slug paths).
/// </summary>
public static class SlugGenerator
{
    private static readonly Regex WordDelimiters = new(@"[\s\u2014\u2013_\/]", RegexOptions.Compiled);
    private static readonly Regex InvalidChars = new(@"[^a-z0-9\-]", RegexOptions.Compiled);
    private static readonly Regex MultipleHyphens = new(@"-{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes text into a URL-friendly slug (lowercase, no diacritics, hyphens only).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var s = value.ToLowerInvariant();
        s = RemoveDiacritics(s);
        s = WordDelimiters.Replace(s, "-");
        s = InvalidChars.Replace(s, "");
        s = MultipleHyphens.Replace(s, "-");
        return s.Trim('-');
    }

    /// <summary>
    /// Ensures a base slug is unique by appending a numeric suffix if needed.
    /// </summary>
    /// <param name="baseSlug">Normalized slug to check.</param>
    /// <param name="isSlugTaken">Returns true if the given slug is already in use (caller handles excludeId logic).</param>
    /// <param name="fallback">Fallback slug if baseSlug is empty.</param>
    public static async Task<string> EnsureUniqueAsync(
        string baseSlug,
        Func<string, Task<bool>> isSlugTaken,
        string fallback = "item")
    {
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = fallback;

        var slug = baseSlug;
        var suffix = 1;

        while (await isSlugTaken(slug))
        {
            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }

        return slug;
    }

    /// <summary>
    /// Synchronous version of <see cref="EnsureUniqueAsync"/>.
    /// </summary>
    public static string EnsureUnique(
        string baseSlug,
        Func<string, bool> isSlugTaken,
        string fallback = "item")
    {
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = fallback;

        var slug = baseSlug;
        var suffix = 1;

        while (isSlugTaken(slug))
        {
            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }

        return slug;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
