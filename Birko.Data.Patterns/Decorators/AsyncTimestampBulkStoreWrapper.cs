using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async bulk store wrapper that automatically sets CreatedAt/UpdatedAt/PrevUpdatedAt timestamps.
/// </summary>
public class AsyncTimestampBulkStoreWrapper<TStore, T> : AsyncTimestampStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : AbstractModel, ITimestamped
{
    public AsyncTimestampBulkStoreWrapper(TStore innerStore, IDateTimeProvider clock) : base(innerStore, clock) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);
    }

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return _innerStore.CreateAsync(data.Select(item =>
        {
            item.CreatedAt = now;
            item.UpdatedAt = now;
            item.PrevUpdatedAt = null;
            return item;
        }), storeDelegate, ct);
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return _innerStore.UpdateAsync(data.Select(item =>
        {
            item.PrevUpdatedAt = item.UpdatedAt;
            item.UpdatedAt = now;
            return item;
        }), storeDelegate, ct);
    }

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
}
