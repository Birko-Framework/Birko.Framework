namespace Birko.Data.Patterns.Concurrency;

/// <summary>
/// Marks an entity as having optimistic concurrency control via a version field.
/// The version is incremented on each update and checked before writes to detect conflicts.
/// </summary>
public interface IVersioned
{
    /// <summary>
    /// Gets or sets the concurrency version. Incremented on each successful update.
    /// </summary>
    long Version { get; set; }
}
