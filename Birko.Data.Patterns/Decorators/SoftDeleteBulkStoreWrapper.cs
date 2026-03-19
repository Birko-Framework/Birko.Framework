using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Bulk store wrapper that adds soft-delete behavior.
/// </summary>
public class SoftDeleteBulkStoreWrapper<TStore, T> : SoftDeleteStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    public SoftDeleteBulkStoreWrapper(TStore innerStore, IDateTimeProvider clock) : base(innerStore, clock) { }

    public IEnumerable<T> Read()
    {
        return Read(null, null, null, null);
    }

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(SoftDeleteFilter.CombineWithNotDeleted(filter), orderBy, limit, offset);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Create(data.Select(item => { item.DeletedAt = null; return item; }), storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Update(data, storeDelegate);
    }

    /// <summary>
    /// Soft-deletes multiple entities.
    /// </summary>
    public void Delete(IEnumerable<T> data)
    {
        var now = _clock.UtcNow;
        var items = data.Select(item => { item.DeletedAt = now; return item; });
        _innerStore.Update(items);
    }
}
