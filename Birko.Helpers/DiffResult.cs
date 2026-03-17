using System.Collections.Generic;

namespace Birko.Helpers;

/// <summary>
/// Result of diffing two collections by key.
/// </summary>
public class DiffResult<T>
{
    /// <summary>
    /// Items present in the desired collection but not in the current collection.
    /// </summary>
    public IReadOnlyList<T> Added { get; }

    /// <summary>
    /// Items present in the current collection but not in the desired collection.
    /// </summary>
    public IReadOnlyList<T> Removed { get; }

    /// <summary>
    /// Items present in both collections (matched by key).
    /// </summary>
    public IReadOnlyList<T> Unchanged { get; }

    public DiffResult(IReadOnlyList<T> added, IReadOnlyList<T> removed, IReadOnlyList<T> unchanged)
    {
        Added = added;
        Removed = removed;
        Unchanged = unchanged;
    }
}
