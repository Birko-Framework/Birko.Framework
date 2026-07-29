using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// A store wrapper that automatically filters by tenant
/// </summary>
public class AsyncTenantStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ITenant
{
    protected readonly TStore _innerStore;
    protected readonly ITenantContext _tenantContext;
    protected readonly TenantIsolationMode _mode;

    /// <summary>
    /// Create a new tenant-aware store wrapper
    /// </summary>
    public AsyncTenantStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _tenantContext = tenantContext ?? Models.Tenant.Current;
        _mode = mode;
    }

    /// <summary>
    /// Create a new item (automatically sets TenantGuid if available)
    /// </summary>
    public async Task<Guid> CreateAsync(T item, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
    {
        SetTenantGuidIfNeeded(item);
        return await _innerStore.CreateAsync(item, processDelegate, cancellationToken);
    }

    /// <summary>
    /// Read an item by GUID (only if it belongs to the current tenant)
    /// </summary>
    public async Task<T?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ReadAsync((new ModelByGuid<T>(id)).Filter(), cancellationToken);
    }

    public async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        return await _innerStore.ReadAsync(TenantFilter(filter).Filter(), cancellationToken);
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        return await _innerStore.CountAsync(TenantFilter(filter).Filter(), cancellationToken);
    }

    /// <summary>
    /// Update an item (only if it belongs to the current tenant)
    /// </summary>
    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (!BelongsToCurrentTenant(data))
        {
            throw new TenantMismatchException(
                "update", typeof(T).Name, _tenantContext.CurrentTenantGuid, data?.TenantGuid);
        }
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    /// <summary>
    /// Delete an item (only if it belongs to the current tenant)
    /// </summary>
    public async Task DeleteAsync(T item, CancellationToken cancellationToken = default)
    {
        if (!BelongsToCurrentTenant(item))
        {
            throw new TenantMismatchException(
                "delete", typeof(T).Name, _tenantContext.CurrentTenantGuid, item?.TenantGuid);
        }

        await _innerStore.DeleteAsync(item, cancellationToken);
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
    {
        if (data == null)
        {
            return Guid.Empty;
        }

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            // CR-M174: return the CreateAsync result directly (mirroring the sync wrapper) rather than
            // relying on the inner store mutating data.Guid in place — the IAsyncStore contract does
            // not guarantee write-back, so a store that allocates the id internally would be lost.
            return await CreateAsync(data, processDelegate, cancellationToken);
        }

        await UpdateAsync(data, processDelegate, cancellationToken);
        return data.Guid ?? Guid.Empty;
    }

    public async Task InitAsync(CancellationToken cancellationToken = default)
    {
        await _innerStore.InitAsync(cancellationToken);
    }

    public async Task DestroyAsync(CancellationToken cancellationToken = default)
    {
        await _innerStore.DestroyAsync(cancellationToken);
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
    /// Builds the tenant read filter. This is the single seam through which every read/count and
    /// every filter-based write composes the tenant predicate — override it to supply a custom
    /// filter strategy (STORY-044). In <see cref="TenantIsolationMode.Strict"/> it throws when no
    /// tenant is in scope rather than returning the caller's unscoped filter (fail-closed).
    /// </summary>
    protected virtual IFilter<T> TenantFilter(Expression<Func<T, bool>>? filter)
    {
        EnsureTenantForStrict();
        // Inside an explicit all-tenants (admin) scope, reads span ALL tenants even when a tenant is
        // also set — otherwise WithAllTenants(...) would silently keep scoping reads to the ambient
        // tenant, contradicting its documented "operate across tenants on purpose" intent. Only the
        // read/count/filter-write path flows through here; the write-authorization guards
        // (BelongsToCurrentTenant / SetTenantGuidIfNeeded) already special-case all-tenants scope.
        var effectiveTenant = _tenantContext.IsAllTenantsScope ? (Guid?)null : _tenantContext.CurrentTenantGuid;
        return new Filters.ModelByTenant<T>(effectiveTenant, filter);
    }

    /// <summary>
    /// In <see cref="TenantIsolationMode.Strict"/>, throws <see cref="TenantScopeRequiredException"/> when no
    /// tenant is in scope. No-op in <see cref="TenantIsolationMode.Permissive"/> (preserves the fail-open
    /// default).
    /// </summary>
    protected void EnsureTenantForStrict()
    {
        if (_mode == TenantIsolationMode.Strict && !_tenantContext.HasTenant && !_tenantContext.IsAllTenantsScope)
        {
            throw new TenantScopeRequiredException("read", typeof(T).Name,
                "Tenant isolation is Strict but no tenant is in scope. Set a tenant, or wrap the " +
                "operation in ITenantContext.WithAllTenants(...) for deliberate cross-tenant access.");
        }
    }

    /// <summary>
    /// Check if an item belongs to the current tenant.
    /// </summary>
    /// <remarks>
    /// With no tenant set (<c>HasTenant == false</c>) the result depends on the isolation mode:
    /// <see cref="TenantIsolationMode.Permissive"/> returns true ("non-tenant/admin mode" — single
    /// and bulk Update/Delete operate across ALL tenants; deliberate fail-open, CR-L229), while
    /// <see cref="TenantIsolationMode.Strict"/> returns false so the caller's guard rejects the
    /// write (fail-closed). Virtual so consumers can derive and override. Behavior is pinned by
    /// explicit tests.
    /// </remarks>
    protected virtual bool BelongsToCurrentTenant(T item)
    {
        // No tenant set: an explicit all-tenants scope always allows; otherwise Permissive is admin
        // mode (allow) and Strict denies (the caller's guard then throws).
        if (!_tenantContext.HasTenant)
        {
            return _tenantContext.IsAllTenantsScope || _mode != TenantIsolationMode.Strict;
        }

        return item.TenantGuid == _tenantContext.CurrentTenantGuid;
    }

    /// <summary>
    /// Set the TenantGuid on an item. In <see cref="TenantIsolationMode.Strict"/> with no tenant in
    /// scope this throws rather than stamping <c>Guid.Empty</c> (which would create orphan rows
    /// invisible to tenant-filtered reads). Inside an all-tenants (admin) scope it trusts the
    /// caller's per-item TenantGuid and leaves it untouched.
    /// </summary>
    protected void SetTenantGuidIfNeeded(T item)
    {
        if (!_tenantContext.HasTenant)
        {
            // Admin scope: the caller owns the per-item TenantGuid; don't stamp/overwrite it.
            if (_tenantContext.IsAllTenantsScope)
            {
                return;
            }

            if (_mode == TenantIsolationMode.Strict)
            {
                throw new TenantScopeRequiredException("create", typeof(T).Name,
                    "Tenant isolation is Strict but no tenant is in scope; refusing to stamp Guid.Empty. " +
                    "Use ITenantContext.WithAllTenants(...) for deliberate cross-tenant writes.");
            }
        }

        item.TenantGuid = _tenantContext.CurrentTenantGuid ?? Guid.Empty;
        item.TenantName = _tenantContext.CurrentTenantName;
    }
}
