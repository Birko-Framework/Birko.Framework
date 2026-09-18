using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Bulk store wrapper that automatically sets CreatedAt/UpdatedAt/PrevUpdatedAt timestamps.
/// </summary>
public class TimestampBulkStoreWrapper<TStore, T> : TimestampStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : AbstractModel, ITimestamped
{
    public TimestampBulkStoreWrapper(TStore innerStore, IDateTimeProvider clock) : base(innerStore, clock) { }

    public IEnumerable<T> Read() => _innerStore.Read();

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(filter, orderBy, limit, offset);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var now = _clock.UtcNow;
        _innerStore.Create(data.Select(item =>
        {
            item.CreatedAt = now;
            item.UpdatedAt = now;
            item.PrevUpdatedAt = null;
            return item;
        }), storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var now = _clock.UtcNow;
        _innerStore.Update(data.Select(item =>
        {
            item.PrevUpdatedAt = item.UpdatedAt;
            item.UpdatedAt = now;
            return item;
        }), storeDelegate);
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        var now = _clock.UtcNow;
        _innerStore.Update(filter, item =>
        {
            updateAction(item);
            item.PrevUpdatedAt = item.UpdatedAt;
            item.UpdatedAt = now;
        });
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        updates.Set(x => x.UpdatedAt, _clock.UtcNow);
        _innerStore.Update(filter, updates);
    }

    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);
    public void Delete(Expression<Func<T, bool>> filter) => _innerStore.Delete(filter);
}
