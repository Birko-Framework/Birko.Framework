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
        var stored = ReadStoredItems(items);
        foreach (var item in items)
        {
            EnsureWriteAuthorized("delete", item, stored);
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
        var stored = ReadStoredItems(items);
        // Authorize the whole batch before mutating any of it, so a refusal in the middle of the set
        // cannot leave earlier items re-stamped.
        foreach (var item in items)
        {
            EnsureWriteAuthorized("update", item, stored);
        }
        foreach (var item in items)
        {
            PreserveStoredTenant(item, stored);
        }

        _innerStore.Update(items, storeDelegate);
    }

    /// <summary>
    /// One read for the whole batch instead of the base class's read-per-item. Same contract: keyed by
    /// Guid, missing rows absent, and deliberately unscoped by tenant (see the base implementation).
    /// </summary>
    protected override IReadOnlyDictionary<Guid, T> ReadStoredItems(IReadOnlyCollection<T> items)
    {
        var guids = TargetGuids(items).ToList();
        var stored = new Dictionary<Guid, T>();
        if (guids.Count == 0)
        {
            return stored;
        }

        // _innerStore is IBulkStore<T> here, so Read(filter) binds to the collection overload.
        foreach (var row in _innerStore.Read(new Data.Filters.ModelsByGuid<T>(guids).Filter()))
        {
            if (row?.Guid != null)
            {
                stored[row.Guid.Value] = row;
            }
        }
        return stored;
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

