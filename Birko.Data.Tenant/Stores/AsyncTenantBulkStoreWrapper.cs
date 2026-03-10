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
    public AsyncTenantBulkStoreWrapper(TStore innerStore, ITenantContext tenantContext = null) : base(innerStore, tenantContext)
    {
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T> storeDelegate = null, CancellationToken cancellationToken = default)
    {
        await _innerStore.CreateAsync(data.Select(item => { SetTenantIdIfNeeded(item); return item; }), storeDelegate, cancellationToken);
    }

    public async Task DeleteAsync(IEnumerable<T> data, CancellationToken cancellationToken = default)
    {
        if (!data.All(BelongsToCurrentTenant))
        {
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
        }

        await _innerStore.DeleteAsync(data, cancellationToken);
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
        return await _innerStore.ReadAsync((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantId, filter)).Filter(), orderBy, limit, offset, cancellationToken);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T> storeDelegate = null, CancellationToken cancellationToken = default)
    {
        if (!data.All(BelongsToCurrentTenant))
        {
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
        }

        await _innerStore.UpdateAsync(data, storeDelegate, cancellationToken);
    }
}

