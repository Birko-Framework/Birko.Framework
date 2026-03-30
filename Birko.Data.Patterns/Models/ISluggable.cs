namespace Birko.Data.Patterns.Models;

/// <summary>
/// Marks an entity as having a URL-friendly slug.
/// The slug store wrapper auto-generates and ensures uniqueness.
/// </summary>
public interface ISluggable
{
    /// <summary>
    /// URL-friendly identifier (e.g. "wireless-mouse", "electronics").
    /// Set by the slug store wrapper on Create/Update.
    /// </summary>
    string? Slug { get; set; }

    /// <summary>
    /// Returns the source text to generate a slug from (typically Name or Title).
    /// Called by the wrapper when Slug is null or empty.
    /// </summary>
    string? GetSlugSource();
}
