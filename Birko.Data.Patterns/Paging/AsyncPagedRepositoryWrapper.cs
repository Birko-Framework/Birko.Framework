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

        var offset = (page - 1) * pageSize;

        // Await sequentially rather than concurrently: the wrapped repository may be backed by a
        // non-thread-safe store/connection that cannot service two in-flight calls on one instance
        // (the count and the page read share the same _repository). Correctness over the marginal
        // latency win. (CR-L159)
        var items = (await _repository.ReadAsync(filter, orderBy, pageSize, offset, ct)).ToList();
        var totalCount = await _repository.CountAsync(filter, ct);

        return new PagedResult<T>(items, totalCount, page, pageSize);
    }
}
