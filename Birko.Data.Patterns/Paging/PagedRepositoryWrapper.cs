using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.Patterns.Paging;

/// <summary>
/// Wraps an <see cref="IBulkRepository{T}"/> to provide synchronous paged read operations.
/// Combines the repository's <see cref="IBulkReadRepository{T}.Read"/> and
/// <see cref="ICountRepository{T}.Count"/> methods to produce <see cref="PagedResult{T}"/>.
/// </summary>
/// <typeparam name="T">The type of entity, must inherit from <see cref="Data.Models.AbstractModel"/>.</typeparam>
public class PagedRepositoryWrapper<T> : IPagedRepository<T>
    where T : Data.Models.AbstractModel
{
    private readonly IBulkRepository<T> _repository;

    /// <summary>
    /// Initializes a new instance wrapping the specified bulk repository.
    /// </summary>
    /// <param name="repository">The bulk repository providing read and count operations.</param>
    public PagedRepositoryWrapper(IBulkRepository<T> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public PagedResult<T> ReadPaged(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        // SH-M300 (TASK-171): (page - 1) * pageSize wrapped to a negative OFFSET for a large page. A page past
        // every possible row is empty.
        var wideOffset = (long)(page - 1) * pageSize;
        if (wideOffset > int.MaxValue)
            return new PagedResult<T>(new System.Collections.Generic.List<T>(), _repository.Count(filter), page, pageSize);
        var offset = (int)wideOffset;
        // SH-M301 (TASK-171): LIMIT/OFFSET with no ORDER BY has no defined row order, so a row could appear on two
        // pages and another on none. The Guid makes every order total; a caller's order keeps precedence.
        orderBy = PagingOrder.Total(orderBy);
        var items = _repository.Read(filter, orderBy, pageSize, offset).ToList();
        var totalCount = _repository.Count(filter);

        return new PagedResult<T>(items, totalCount, page, pageSize);
    }
}
