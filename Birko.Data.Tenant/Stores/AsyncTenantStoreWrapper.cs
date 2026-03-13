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

    /// <summary>
    /// Create a new tenant-aware store wrapper
    /// </summary>
    public AsyncTenantStoreWrapper(TStore innerStore, ITenantContext? tenantContext = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _tenantContext = tenantContext ?? Models.Tenant.Current;
    }

    /// <summary>
    /// Create a new item (automatically sets TenantId if available)
    /// </summary>
    public async Task<Guid> CreateAsync(T item, StoreDataDelegate<T>? processDelegate = null, CancellationToken cancellationToken = default)
    {
        SetTenantIdIfNeeded(item);
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
        return await _innerStore.ReadAsync((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantId, filter)).Filter(), cancellationToken);
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        return await _innerStore.CountAsync((new Filters.ModelByTenant<T>(_tenantContext.CurrentTenantId, filter)).Filter(), cancellationToken);
    }

    /// <summary>
    /// Update an item (only if it belongs to the current tenant)
    /// </summary>
    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (!BelongsToCurrentTenant(data))
        {
            throw new UnauthorizedAccessException(
                $"Cannot update item: it does not belong to the current tenant"
            );
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
            throw new UnauthorizedAccessException(
                $"Cannot delete item: it does not belong to the current tenant"
            );
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
            await CreateAsync(data, processDelegate, cancellationToken);
        }
        else
        {
            await UpdateAsync(data, processDelegate, cancellationToken);
        }

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
