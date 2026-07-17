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
    protected readonly TenantIsolationMode _mode;

    /// <summary>
    /// Create a new tenant-aware store wrapper
    /// </summary>
    public TenantStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _tenantContext = tenantContext ?? Models.Tenant.Current;
        _mode = mode;
    }

    /// <summary>
    /// Create a new item (automatically sets TenantGuid if available)
    /// </summary>
    public Guid Create(T item, StoreDataDelegate<T>? processDelegate = null)
    {
        SetTenantGuidIfNeeded(item);
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
        return _innerStore.Read(TenantFilter(filter).Filter());
    }

    public T? ReadOne(Expression<Func<T, bool>>? filter = null)
    {
        return Read(filter);
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        return _innerStore.Count(TenantFilter(filter).Filter());
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
    /// Builds the tenant read filter — the single seam every read/count composes the tenant predicate
    /// through (override to supply a custom strategy). In <see cref="TenantIsolationMode.Strict"/> it
    /// throws when no tenant is in scope (unless inside an all-tenants scope) instead of returning
    /// the caller's unscoped filter (STORY-044, sync parity with the async wrapper).
    /// </summary>
    protected virtual IFilter<T> TenantFilter(Expression<Func<T, bool>>? filter)
    {
        EnsureTenantForStrict();
        return new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantGuid, filter);
    }

    /// <summary>
    /// In <see cref="TenantIsolationMode.Strict"/>, throws when no tenant is in scope and no explicit
    /// all-tenants scope is active. No-op in <see cref="TenantIsolationMode.Permissive"/>.
    /// </summary>
    protected void EnsureTenantForStrict()
    {
        if (_mode == TenantIsolationMode.Strict && !_tenantContext.HasTenant && !_tenantContext.IsAllTenantsScope)
        {
            throw new InvalidOperationException(
                "Tenant isolation is Strict but no tenant is in scope. Set a tenant, or wrap the " +
                "operation in ITenantContext.WithAllTenants(...) for deliberate cross-tenant access.");
        }
    }

    /// <summary>
    /// Check if an item belongs to the current tenant.
    /// </summary>
    /// <remarks>
    /// With no tenant set the result depends on the isolation mode:
    /// <see cref="TenantIsolationMode.Permissive"/> returns true ("non-tenant/admin mode", CR-L229),
    /// while <see cref="TenantIsolationMode.Strict"/> returns false (fail-closed) unless an explicit
    /// all-tenants scope is active. Virtual so consumers can derive and override.
    /// </remarks>
    protected virtual bool BelongsToCurrentTenant(T item)
    {
        if (!_tenantContext.HasTenant)
        {
            return _tenantContext.IsAllTenantsScope || _mode != TenantIsolationMode.Strict;
        }

        return item.TenantGuid == _tenantContext.CurrentTenantGuid;
    }

    /// <summary>
    /// Set the TenantGuid on an item. In <see cref="TenantIsolationMode.Strict"/> with no tenant in
    /// scope this throws rather than stamping <c>Guid.Empty</c>; inside an all-tenants scope it trusts
    /// the caller's per-item TenantGuid and leaves it untouched.
    /// </summary>
    protected void SetTenantGuidIfNeeded(T item)
    {
        if (!_tenantContext.HasTenant)
        {
            if (_tenantContext.IsAllTenantsScope)
            {
                return;
            }

            if (_mode == TenantIsolationMode.Strict)
            {
                throw new InvalidOperationException(
                    "Tenant isolation is Strict but no tenant is in scope; refusing to stamp Guid.Empty. " +
                    "Use ITenantContext.WithAllTenants(...) for deliberate cross-tenant writes.");
            }
        }

        item.TenantGuid = _tenantContext.CurrentTenantGuid ?? Guid.Empty;
        item.TenantName = _tenantContext.CurrentTenantName;
    }
}
