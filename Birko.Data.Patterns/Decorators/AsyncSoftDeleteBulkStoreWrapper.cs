using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async bulk store wrapper that adds soft-delete behavior.
/// </summary>
public class AsyncSoftDeleteBulkStoreWrapper<TStore, T> : AsyncSoftDeleteStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    public AsyncSoftDeleteBulkStoreWrapper(TStore innerStore) : base(innerStore) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
    {
        return ReadAsync(null, null, null, null, ct);
    }

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), orderBy, limit, offset, ct);
    }

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        return _innerStore.CreateAsync(data.Select(item => { item.DeletedAt = null; return item; }), storeDelegate, ct);
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        return _innerStore.UpdateAsync(data, storeDelegate, ct);
    }

    /// <summary>
    /// Soft-deletes multiple entities.
    /// </summary>
    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var items = data.Select(item => { item.DeletedAt = now; return item; });
        return _innerStore.UpdateAsync(items, ct: ct);
    }
}
