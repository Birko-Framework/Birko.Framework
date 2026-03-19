using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Bulk store wrapper that automatically sets CreatedBy/UpdatedBy from IAuditContext.
/// </summary>
public class AuditBulkStoreWrapper<TStore, T> : AuditStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, IAuditable
{
    public AuditBulkStoreWrapper(TStore innerStore, IAuditContext auditContext) : base(innerStore, auditContext) { }

    public IEnumerable<T> Read() => _innerStore.Read();

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(filter, orderBy, limit, offset);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var userId = _auditContext.CurrentUserId;
        _innerStore.Create(data.Select(item =>
        {
            item.CreatedBy = userId;
            item.UpdatedBy = userId;
            return item;
        }), storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var userId = _auditContext.CurrentUserId;
        _innerStore.Update(data.Select(item =>
        {
            item.UpdatedBy = userId;
            return item;
        }), storeDelegate);
    }

    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);
}
