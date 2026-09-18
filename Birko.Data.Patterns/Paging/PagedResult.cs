using System;
using System.Collections.Generic;

namespace Birko.Data.Patterns.Paging;

/// <summary>
/// Represents a page of results with total count metadata.
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>
    /// The items in this page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Total number of items across all pages (before pagination).
    /// </summary>
    public long TotalCount { get; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    public PagedResult(IReadOnlyList<T> items, long totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// Creates an empty paged result.
    /// </summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 20)
        => new([], 0, page, pageSize);
}
