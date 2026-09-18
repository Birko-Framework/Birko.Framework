using System.Collections.Generic;

namespace Birko.Data.Views;

/// <summary>
/// Typed result from a view query, optionally including total count for pagination.
/// </summary>
public sealed class ViewResult<TView> where TView : class
{
    /// <summary>The result items.</summary>
    public IReadOnlyList<TView> Items { get; }

    /// <summary>Total count of matching items (null if not requested).</summary>
    public long? TotalCount { get; }

    public ViewResult(IReadOnlyList<TView> items, long? totalCount = null)
    {
        Items = items;
        TotalCount = totalCount;
    }

    public static ViewResult<TView> Empty { get; } = new([], 0);
}
