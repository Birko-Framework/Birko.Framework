using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.Patterns.Paging;

/// <summary>
/// Defines synchronous paged read operations.
/// </summary>
/// <typeparam name="T">The type of entity, must inherit from <see cref="Data.Models.AbstractModel"/>.</typeparam>
public interface IPagedRepository<T>
    where T : Data.Models.AbstractModel
{
    /// <summary>
    /// Reads a page of entities matching the specified filter.
    /// </summary>
    /// <param name="filter">Optional filter expression.</param>
    /// <param name="orderBy">Optional sort specification.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>A paged result containing the items and total count metadata.</returns>
    PagedResult<T> ReadPaged(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int page = 1,
        int pageSize = 20);
}

/// <summary>
/// Defines asynchronous paged read operations.
/// </summary>
/// <typeparam name="T">The type of entity, must inherit from <see cref="Data.Models.AbstractModel"/>.</typeparam>
public interface IAsyncPagedRepository<T>
    where T : Data.Models.AbstractModel
{
    /// <summary>
    /// Asynchronously reads a page of entities matching the specified filter.
    /// </summary>
    /// <param name="filter">Optional filter expression.</param>
    /// <param name="orderBy">Optional sort specification.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A paged result containing the items and total count metadata.</returns>
    Task<PagedResult<T>> ReadPagedAsync(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
}
