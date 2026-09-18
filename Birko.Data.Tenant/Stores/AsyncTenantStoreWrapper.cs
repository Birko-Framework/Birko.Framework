using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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
        var stored = await ReadStoredItemsAsync(AsTargets(data), ct);
        EnsureWriteAuthorized("update", data, stored);
        PreserveStoredTenant(data, stored);
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    /// <summary>
    /// Delete an item (only if it belongs to the current tenant)
    /// </summary>
    public async Task DeleteAsync(T item, CancellationToken cancellationToken = default)
    {
        var stored = await ReadStoredItemsAsync(AsTargets(item), cancellationToken);
        EnsureWriteAuthorized("delete", item, stored);
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
    /// The distinct, non-empty Guids an item-level write targets. An item carrying no Guid targets no
    /// persisted row, so it contributes nothing to look up.
    /// </summary>
    protected static IEnumerable<Guid> TargetGuids(IReadOnlyCollection<T> items)
    {
        return items
            .Where(i => i != null && i.Guid != null && i.Guid != Guid.Empty)
            .Select(i => i.Guid!.Value)
            .Distinct();
    }

    /// <summary>Wraps a single item as the target set of an item-level write. A null item targets nothing.</summary>
    protected static IReadOnlyCollection<T> AsTargets(T? item)
    {
        return item == null ? Array.Empty<T>() : new[] { item };
    }

    /// <summary>
    /// Reads the persisted rows an item-level write targets, keyed by Guid. A Guid absent from the result
    /// has no row behind it.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately unscoped by tenant (SH-H047).</b> The guard needs each row's <i>real</i>
    /// tenant, and a tenant-scoped read cannot supply it: another tenant's row would come back
    /// <c>null</c>, indistinguishable from a row that does not exist — and "does not exist" authorizes the
    /// write, which is exactly the overwrite this guard exists to prevent.</para>
    /// <para>One read per item here; <see cref="AsyncTenantBulkStoreWrapper{TStore, T}"/> overrides this
    /// with a single <see cref="ModelsByGuid{TModel}"/> read for the batch paths.</para>
    /// </remarks>
    protected virtual async Task<IReadOnlyDictionary<Guid, T>> ReadStoredItemsAsync(
        IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        var stored = new Dictionary<Guid, T>();
        foreach (var guid in TargetGuids(items))
        {
            // Cast to IAsyncReadStore<T> so this binds to the single-result ReadAsync(filter) even when
            // the inner store is a bulk store, whose own ReadAsync(filter) overload returns a collection.
            var row = await ((IAsyncReadStore<T>)_innerStore)
                .ReadAsync(new ModelByGuid<T>(guid).Filter(), cancellationToken);
            if (row != null)
            {
                stored[guid] = row;
            }
        }
        return stored;
    }

    /// <summary>
    /// Authorizes an item-level write against the <b>persisted</b> row rather than the caller-supplied item.
    /// </summary>
    /// <remarks>
    /// <para>SH-H047. <see cref="ITenant.TenantGuid"/> is a public settable property, routinely model-bound
    /// straight from a request body — it is the caller's <i>assertion</i> about a row, not a fact about it.
    /// Comparing it to the ambient tenant let a caller in tenant <i>t</i> submit
    /// <c>{ Guid = &lt;a row belonging to another tenant&gt;, TenantGuid = t }</c>, pass the guard, and have
    /// the inner store — which keys the write on the primary field alone — overwrite or delete that row.</para>
    /// <para><see cref="BelongsToCurrentTenant"/> keeps its meaning and stays the consumer override seam;
    /// what changed is its <i>subject</i> whenever a row exists — from the caller's claim to the stored
    /// row.</para>
    /// <para>The pre-existing payload check survives, but only for the case where <b>no row exists</b>. It
    /// was never authorization — a caller sets <c>TenantGuid</c> to whatever passes — so it is kept for
    /// what it is actually worth: an inner store that upserts cannot be made to create a row homed in
    /// another tenant, and the documented refusal keeps its shape for honest callers.</para>
    /// </remarks>
    protected void EnsureWriteAuthorized(string operation, T? item, IReadOnlyDictionary<Guid, T> stored)
    {
        var guid = item?.Guid;
        if (guid != null && guid != Guid.Empty && stored.TryGetValue(guid.Value, out var row))
        {
            // A row exists under that Guid: it, and only it, decides. item.TenantGuid is not consulted.
            if (BelongsToCurrentTenant(row))
            {
                return;
            }

            throw new TenantMismatchException(
                operation, typeof(T).Name, _tenantContext.CurrentTenantGuid, row.TenantGuid);
        }

        // Nothing persisted under that Guid — no foreign row is at risk and there is nothing to authorize
        // against, so fall back to the payload consistency check described above.
        if (item == null || BelongsToCurrentTenant(item))
        {
            return;
        }

        throw new TenantMismatchException(
            operation, typeof(T).Name, _tenantContext.CurrentTenantGuid, item.TenantGuid);
    }

    /// <summary>
    /// Restores the persisted tenant onto an item before an update, so an owner cannot <i>re-home</i> a row
    /// into another tenant by editing <c>TenantGuid</c> in the payload.
    /// </summary>
    /// <remarks>
    /// Skipped when no tenant is in scope and inside <c>WithAllTenants</c> — those are the documented
    /// deliberate-cross-tenant scopes, and <see cref="SetTenantGuidIfNeeded"/> already leaves the caller's
    /// per-item TenantGuid untouched there on create.
    /// </remarks>
    protected void PreserveStoredTenant(T? item, IReadOnlyDictionary<Guid, T> stored)
    {
        if (item == null || !_tenantContext.HasTenant || _tenantContext.IsAllTenantsScope)
        {
            return;
        }

        var guid = item.Guid;
        if (guid == null || guid == Guid.Empty || !stored.TryGetValue(guid.Value, out var row))
        {
            return;
        }

        item.TenantGuid = row.TenantGuid;
        item.TenantName = row.TenantName;
    }

    /// <summary>
    /// Check if an item belongs to the current tenant.
    /// </summary>
    /// <remarks>
    /// <b>Pass the persisted row, never the caller-supplied item</b> — the write paths call this through
    /// <see cref="EnsureWriteAuthorized"/> with the row read back from the store (SH-H047). An override that
    /// consults anything the caller controls re-opens the cross-tenant overwrite.
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
