using Birko.Data.Filters;
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
        var stored = ReadStoredItems(AsTargets(data));
        EnsureWriteAuthorized("update", data, stored);
        PreserveStoredTenant(data, stored);
        _innerStore.Update(data, processDelegate);
    }

    /// <summary>
    /// Delete an item (only if it belongs to the current tenant)
    /// </summary>
    public void Delete(T item)
    {
        EnsureWriteAuthorized("delete", item, ReadStoredItems(AsTargets(item)));
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
    /// tenant is in scope and no explicit all-tenants scope is active. No-op in
    /// <see cref="TenantIsolationMode.Permissive"/>.
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
    /// <b>Pass the persisted row, never the caller-supplied item</b> — the write paths call this through
    /// <see cref="EnsureWriteAuthorized"/> with the row read back from the store (SH-H047). An override that
    /// consults anything the caller controls re-opens the cross-tenant overwrite.
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
    /// <para>One read per item here; <see cref="TenantBulkStoreWrapper{TStore, T}"/> overrides this with a
    /// single <see cref="ModelsByGuid{TModel}"/> read for the batch paths.</para>
    /// </remarks>
    protected virtual IReadOnlyDictionary<Guid, T> ReadStoredItems(IReadOnlyCollection<T> items)
    {
        var stored = new Dictionary<Guid, T>();
        foreach (var guid in TargetGuids(items))
        {
            // Cast to IReadStore<T> so this binds to the single-result Read(filter) even when the inner
            // store is a bulk store, whose own Read(filter) overload returns a collection.
            var row = ((IReadStore<T>)_innerStore).Read(new ModelByGuid<T>(guid).Filter());
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
                throw new TenantScopeRequiredException("create", typeof(T).Name,
                    "Tenant isolation is Strict but no tenant is in scope; refusing to stamp Guid.Empty. " +
                    "Use ITenantContext.WithAllTenants(...) for deliberate cross-tenant writes.");
            }
        }

        item.TenantGuid = _tenantContext.CurrentTenantGuid ?? Guid.Empty;
        item.TenantName = _tenantContext.CurrentTenantName;
    }
}
