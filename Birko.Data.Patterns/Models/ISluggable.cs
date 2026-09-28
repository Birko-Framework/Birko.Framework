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

    /// <summary>
    /// Returns true when <paramref name="slug"/> may not be used even though no other row holds it —
    /// typically a literal route segment that shares the URL position the slug is served from
    /// (e.g. <c>/products/{slug}</c> next to <c>/products/facets</c>): routing ranks the literal above the
    /// parameter, so an entity slugged <c>facets</c> could never be fetched by slug.
    /// The wrapper treats a reserved slug exactly like a taken one and de-duplicates it (<c>facets-2</c>).
    /// Receives the NORMALIZED slug. Defaults to reserving nothing.
    /// </summary>
    bool IsReservedSlug(string slug) => false;
}
