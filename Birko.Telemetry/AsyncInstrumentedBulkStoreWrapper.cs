using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Telemetry;

/// <summary>
/// Store wrapper that adds metrics and tracing instrumentation to all <see cref="IAsyncBulkStore{T}"/> operations.
/// </summary>
public class AsyncInstrumentedBulkStoreWrapper<TStore, T> : AsyncInstrumentedStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
{
    public AsyncInstrumentedBulkStoreWrapper(TStore innerStore) : base(innerStore)
    {
    }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Read", true, () => _innerStore.ReadAsync(ct));

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Read", true, () => _innerStore.ReadAsync(filter, orderBy, limit, offset, ct));

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Create", true, () => _innerStore.CreateAsync(data, storeDelegate, ct));

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Update", true, () => _innerStore.UpdateAsync(data, storeDelegate, ct));

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Delete", true, () => _innerStore.DeleteAsync(data, ct));
}
