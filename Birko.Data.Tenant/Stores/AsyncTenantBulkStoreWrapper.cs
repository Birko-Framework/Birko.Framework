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
public class AsyncTenantBulkStoreWrapper<TStore, T> : AsyncTenantStoreWrapper<TStore, T>, IAsyncBulkStore<T>, IStoreWrapper<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ITenant
{
    public AsyncTenantBulkStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive) : base(innerStore, tenantContext, mode)
    {
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken cancellationToken = default)
    {
        await _innerStore.CreateAsync(data.Select(item => { SetTenantGuidIfNeeded(item); return item; }), storeDelegate, cancellationToken);
    }

    public async Task DeleteAsync(IEnumerable<T> data, CancellationToken cancellationToken = default)
    {
        // CR-M173: materialize once — validating then passing the same lazy source enumerated it twice,
        // so a non-deterministic sequence could persist a set different from the one authorized.
        var items = data as IReadOnlyCollection<T> ?? data.ToList();
        var stored = await ReadStoredItemsAsync(items, cancellationToken);
        foreach (var item in items)
        {
            EnsureWriteAuthorized("delete", item, stored);
        }

        await _innerStore.DeleteAsync(items, cancellationToken);
    }

    /// <summary>
    /// Read all items (filtered by current tenant)
    /// </summary>
    public async Task<IEnumerable<T>> ReadAsync(CancellationToken cancellationToken = default)
    {
        return await ReadAsync(null, null, null, null, cancellationToken);
    }

    public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        return await _innerStore.ReadAsync(TenantFilter(filter).Filter(), orderBy, limit, offset, cancellationToken);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken cancellationToken = default)
    {
        var items = data as IReadOnlyCollection<T> ?? data.ToList(); // CR-M173: materialize once
        var stored = await ReadStoredItemsAsync(items, cancellationToken);
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

        await _innerStore.UpdateAsync(items, storeDelegate, cancellationToken);
    }

    /// <summary>
    /// One read for the whole batch instead of the base class's read-per-item. Same contract: keyed by
    /// Guid, missing rows absent, and deliberately unscoped by tenant (see the base implementation).
    /// </summary>
    protected override async Task<IReadOnlyDictionary<Guid, T>> ReadStoredItemsAsync(
        IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        var guids = TargetGuids(items).ToList();
        var stored = new Dictionary<Guid, T>();
        if (guids.Count == 0)
        {
            return stored;
        }

        // _innerStore is IAsyncBulkStore<T> here, so ReadAsync(filter) binds to the collection overload.
        var rows = await _innerStore.ReadAsync(
            new Data.Filters.ModelsByGuid<T>(guids).Filter(), null, null, null, cancellationToken);
        foreach (var row in rows)
        {
            if (row?.Guid != null)
            {
                stored[row.Guid.Value] = row;
            }
        }
        return stored;
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken cancellationToken = default)
    {
        RequireCallerFilter(filter, "update");
        await _innerStore.UpdateAsync(TenantFilter(filter).Filter()!, updateAction, cancellationToken);
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken cancellationToken = default)
    {
        RequireCallerFilter(filter, "update");
        await _innerStore.UpdateAsync(TenantFilter(filter).Filter()!, updates, cancellationToken);
    }

    public async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
    {
        RequireCallerFilter(filter, "delete");
        await _innerStore.DeleteAsync(TenantFilter(filter).Filter()!, cancellationToken);
    }

    // SH-M337 (TASK-161): the caller's filter is checked BEFORE the tenant predicate is added to it. Afterwards a null
    // filter is "this tenant's every row" and `x => true` looks bounded, so the inner store's own guards never fire, and
    // a forwarded null deleted every row of the tenant. The tenant scope is applied for you: name the rows.
    private static void RequireCallerFilter(Expression<Func<T, bool>> filter, string operation)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered {operation} on {typeof(T).Name} needs a filter.");
        Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name,
            "a filter that names the rows (the tenant scope is added to it)");
    }
}
