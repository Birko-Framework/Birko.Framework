namespace Birko.Data.Views;

/// <summary>
/// Controls how a view query is executed.
/// </summary>
public enum ViewQueryMode
{
    /// <summary>
    /// Generate the query at runtime (SELECT with JOINs, aggregation pipeline, etc.).
    /// </summary>
    OnTheFly = 0,

    /// <summary>
    /// Query a pre-created persistent view (SQL VIEW, MongoDB view, RavenDB static index, etc.).
    /// </summary>
    Persistent = 1,

    /// <summary>
    /// Try persistent view first; fall back to on-the-fly if it does not exist.
    /// </summary>
    Auto = 2
}
