using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.Patterns.Paging;

/// <summary>
/// Wraps an <see cref="IAsyncBulkRepository{T}"/> to provide asynchronous paged read operations.
/// Combines the repository's <see cref="IAsyncBulkReadRepository{T}.ReadAsync"/> and
/// <see cref="IAsyncCountRepository{T}.CountAsync"/> methods to produce <see cref="PagedResult{T}"/>.
/// </summary>
/// <typeparam name="T">The type of entity, must inherit from <see cref="Data.Models.AbstractModel"/>.</typeparam>
public class AsyncPagedRepositoryWrapper<T> : IAsyncPagedRepository<T>
    where T : Data.Models.AbstractModel
{
    private readonly IAsyncBulkRepository<T> _repository;

    /// <summary>
    /// Initializes a new instance wrapping the specified async bulk repository.
    /// </summary>
    /// <param name="repository">The async bulk repository providing read and count operations.</param>
    public AsyncPagedRepositoryWrapper(IAsyncBulkRepository<T> repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<PagedResult<T>> ReadPagedAsync(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        // SH-M300 (TASK-171): (page - 1) * pageSize wrapped to a negative OFFSET for a large page. A page past
        // every possible row is empty.
        var wideOffset = (long)(page - 1) * pageSize;
        if (wideOffset > int.MaxValue)
            return new PagedResult<T>(new System.Collections.Generic.List<T>(), await _repository.CountAsync(filter, ct), page, pageSize);
        var offset = (int)wideOffset;
        // SH-M301 (TASK-171): LIMIT/OFFSET with no ORDER BY has no defined row order, so a row could appear on two
        // pages and another on none. The Guid makes every order total; a caller's order keeps precedence.
        orderBy = PagingOrder.Total(orderBy);

        // Await sequentially rather than concurrently: the wrapped repository may be backed by a
        // non-thread-safe store/connection that cannot service two in-flight calls on one instance
        // (the count and the page read share the same _repository). Correctness over the marginal
        // latency win. (CR-L159)
        var items = (await _repository.ReadAsync(filter, orderBy, pageSize, offset, ct)).ToList();
        var totalCount = await _repository.CountAsync(filter, ct);

        return new PagedResult<T>(items, totalCount, page, pageSize);
    }
}
