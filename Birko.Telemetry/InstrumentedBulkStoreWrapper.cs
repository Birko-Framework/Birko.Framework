using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Telemetry;

/// <summary>
/// Store wrapper that adds metrics and tracing instrumentation to all <see cref="IBulkStore{T}"/> operations.
/// </summary>
public class InstrumentedBulkStoreWrapper<TStore, T> : InstrumentedStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel
{
    public InstrumentedBulkStoreWrapper(TStore innerStore) : base(innerStore)
    {
    }

    public IEnumerable<T> Read()
        => StoreInstrumentation.Execute(_storeType, _entityType, "Read", true, () => _innerStore.Read());

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Read", true, () => _innerStore.Read(filter, orderBy, limit, offset));

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Create", true, () => _innerStore.Create(data, storeDelegate));

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Update", true, () => _innerStore.Update(data, storeDelegate));

    public void Delete(IEnumerable<T> data)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Delete", true, () => _innerStore.Delete(data));
}
