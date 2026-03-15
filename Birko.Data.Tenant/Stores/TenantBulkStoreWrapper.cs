using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// A store wrapper that automatically filters by tenant
/// </summary>
public class TenantBulkStoreWrapper<TStore, T> : TenantStoreWrapper<TStore, T>, IBulkStore<T>, IStoreWrapper<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ITenant
{
    public TenantBulkStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null) : base(innerStore, tenantContext)
    {
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Create(data.Select(item => { SetTenantGuidIfNeeded(item); return item; }), storeDelegate);
    }

    public void Delete(IEnumerable<T> data)
    {
        if (!data.All(BelongsToCurrentTenant))
        {
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
        }

        _innerStore.Delete(data);
    }

    /// <summary>
    /// Read all items (filtered by current tenant)
    /// </summary>
    public IEnumerable<T> Read()
    {
        return Read(null, null, null, null);
    }

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantGuid, filter)).Filter(), orderBy, limit, offset);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (!data.All(BelongsToCurrentTenant))
        {
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
        }

        _innerStore.Update(data, storeDelegate);
    }
}

