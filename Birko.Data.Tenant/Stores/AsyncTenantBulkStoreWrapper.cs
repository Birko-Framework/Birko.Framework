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
        if (!items.All(BelongsToCurrentTenant))
        {
            throw new TenantMismatchException(
                "delete", typeof(T).Name, _tenantContext.CurrentTenantGuid,
                items.FirstOrDefault(i => !BelongsToCurrentTenant(i))?.TenantGuid);
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
        if (!items.All(BelongsToCurrentTenant))
        {
            throw new TenantMismatchException(
                "update", typeof(T).Name, _tenantContext.CurrentTenantGuid,
                items.FirstOrDefault(i => !BelongsToCurrentTenant(i))?.TenantGuid);
        }

        await _innerStore.UpdateAsync(items, storeDelegate, cancellationToken);
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken cancellationToken = default)
    {
        await _innerStore.UpdateAsync(TenantFilter(filter).Filter()!, updateAction, cancellationToken);
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken cancellationToken = default)
    {
        await _innerStore.UpdateAsync(TenantFilter(filter).Filter()!, updates, cancellationToken);
    }

    public async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
    {
        await _innerStore.DeleteAsync(TenantFilter(filter).Filter()!, cancellationToken);
    }
}

