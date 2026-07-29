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
    public TenantBulkStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive) : base(innerStore, tenantContext, mode)
    {
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Create(data.Select(item => { SetTenantGuidIfNeeded(item); return item; }), storeDelegate);
    }

    public void Delete(IEnumerable<T> data)
    {
        // CR-M173: materialize once so the authorized set equals the persisted set for a lazy source.
        var items = data as IReadOnlyCollection<T> ?? data.ToList();
        if (!items.All(BelongsToCurrentTenant))
        {
            throw new TenantMismatchException(
                "delete", typeof(T).Name, _tenantContext.CurrentTenantGuid,
                items.FirstOrDefault(i => !BelongsToCurrentTenant(i))?.TenantGuid);
        }

        _innerStore.Delete(items);
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
        return _innerStore.Read(TenantFilter(filter).Filter(), orderBy, limit, offset);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data as IReadOnlyCollection<T> ?? data.ToList(); // CR-M173: materialize once
        if (!items.All(BelongsToCurrentTenant))
        {
            throw new TenantMismatchException(
                "update", typeof(T).Name, _tenantContext.CurrentTenantGuid,
                items.FirstOrDefault(i => !BelongsToCurrentTenant(i))?.TenantGuid);
        }

        _innerStore.Update(items, storeDelegate);
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        _innerStore.Update(TenantFilter(filter).Filter()!, updateAction);
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        _innerStore.Update(TenantFilter(filter).Filter()!, updates);
    }

    public void Delete(Expression<Func<T, bool>> filter)
    {
        _innerStore.Delete(TenantFilter(filter).Filter()!);
    }
}

