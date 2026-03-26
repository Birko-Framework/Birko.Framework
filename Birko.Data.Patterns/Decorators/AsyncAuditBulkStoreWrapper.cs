using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async bulk store wrapper that automatically sets CreatedBy/UpdatedBy from IAuditContext.
/// </summary>
public class AsyncAuditBulkStoreWrapper<TStore, T> : AsyncAuditStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, IAuditable
{
    public AsyncAuditBulkStoreWrapper(TStore innerStore, IAuditContext auditContext) : base(innerStore, auditContext) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);
    }

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var userId = _auditContext.CurrentUserId;
        return _innerStore.CreateAsync(data.Select(item =>
        {
            item.CreatedBy = userId;
            item.UpdatedBy = userId;
            return item;
        }), storeDelegate, ct);
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var userId = _auditContext.CurrentUserId;
        return _innerStore.UpdateAsync(data.Select(item =>
        {
            item.UpdatedBy = userId;
            return item;
        }), storeDelegate, ct);
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        var userId = _auditContext.CurrentUserId;
        return _innerStore.UpdateAsync(filter, item =>
        {
            updateAction(item);
            item.UpdatedBy = userId;
        }, ct);
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        updates.Set(x => x.UpdatedBy, _auditContext.CurrentUserId);
        return _innerStore.UpdateAsync(filter, updates, ct);
    }

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _innerStore.DeleteAsync(filter, ct);
}
