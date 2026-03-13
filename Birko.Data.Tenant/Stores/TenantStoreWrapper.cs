using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// A store wrapper that automatically filters by tenant
/// </summary>
public class TenantStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ITenant
{
    protected readonly TStore _innerStore;
    protected readonly ITenantContext _tenantContext;

    /// <summary>
    /// Create a new tenant-aware store wrapper
    /// </summary>
    public TenantStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _tenantContext = tenantContext ?? Models.Tenant.Current;
    }

    /// <summary>
    /// Create a new item (automatically sets TenantId if available)
    /// </summary>
    public Guid Create(T item, StoreDataDelegate<T>? processDelegate = null)
    {
        SetTenantIdIfNeeded(item);
        return _innerStore.Create(item, processDelegate);
    }

    /// <summary>
    /// Read an item by GUID (only if it belongs to the current tenant)
    /// </summary>
    public T? Read(Guid id)
    {
        return ReadOne((new ModelByGuid<T>(id)).Filter());
    }

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        return _innerStore.Read((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantId, filter)).Filter());
    }

    public T? ReadOne(Expression<Func<T, bool>>? filter = null)
    {
        return Read(filter);
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        return _innerStore.Count((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantId, filter)).Filter());
    }

    /// <summary>
    /// Update an item (only if it belongs to the current tenant)
    /// </summary>
    public void Update(T data, StoreDataDelegate<T>? processDelegate = null)
    {
        if (!BelongsToCurrentTenant(data))
        {
            throw new UnauthorizedAccessException(
                $"Cannot update item: it does not belong to the current tenant"
            );
        }
        _innerStore.Update(data, processDelegate);
    }

    /// <summary>
    /// Delete an item (only if it belongs to the current tenant)
    /// </summary>
    public void Delete(T item)
    {
        if (!BelongsToCurrentTenant(item))
        {
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
        }

        _innerStore.Delete(item);
    }

    public Guid Save(T data, StoreDataDelegate<T>? processDelegate = null)
    {
        if (data == null)
        {
            return Guid.Empty;
        }

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return Create(data, processDelegate);
        }
        else
        {
            Update(data, processDelegate);
            return data.Guid ?? Guid.Empty;
        }
    }

    public void Init()
    {
        _innerStore.Init();
    }

    public void Destroy()
    {
        _innerStore.Destroy();
    }

    public T CreateInstance()
    {
        return _innerStore.CreateInstance();
    }

    /// <summary>
    /// Gets the inner wrapped store.
    /// </summary>
    object? IStoreWrapper.GetInnerStore()
    {
        return _innerStore;
    }

    /// <summary>
    /// Gets the inner wrapped store as the specified type.
    /// </summary>
    public TInner? GetInnerStoreAs<TInner>() where TInner : class
    {
        return _innerStore as TInner;
    }

    /// <summary>
    /// Check if an item belongs to the current tenant
    /// </summary>
    protected bool BelongsToCurrentTenant(T item)
    {
        // If no tenant is set, allow access (non-tenant mode)
        if (!_tenantContext.HasTenant)
        {
            return true;
        }

        return item.TenantId == _tenantContext.CurrentTenantId;
    }

    /// <summary>
    /// Set the TenantId on an item if the property exists and no tenant is set
    /// </summary>
    protected void SetTenantIdIfNeeded(T item)
    {
        item.TenantId = _tenantContext.CurrentTenantId ?? Guid.Empty;
        item.TenantName = _tenantContext.CurrentTenantName;
    }
}
