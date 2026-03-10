using Birko.Data.Stores;
using Birko.Data.Tenant.Models;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// Extension methods for creating tenant-aware stores
/// </summary>
public static class TenantStoreExtensions
{
    /// <summary>
    /// Wrap a store with tenant filtering
    /// </summary>
    public static IAsyncStore<T> AsTenantAware<T>(this IAsyncStore<T> store, ITenantContext? tenantContext = null)
        where T : Data.Models.AbstractModel, ITenant
    {
        return (store is IAsyncBulkStore<T>)
             ? new AsyncTenantBulkStoreWrapper<IAsyncBulkStore<T>, T>((IAsyncBulkStore<T>)store, tenantContext)
             : new AsyncTenantStoreWrapper<IAsyncStore<T>, T>(store, tenantContext);
    }

    /// <summary>
    /// Wrap a store with tenant filtering
    /// </summary>
    public static IStore<T> AsTenantAware<T>(this IStore<T> store, ITenantContext? tenantContext = null)
        where T : Data.Models.AbstractModel, ITenant
    {
        return (store is IBulkStore<T>)
             ? new TenantBulkStoreWrapper<IBulkStore<T>, T>((IBulkStore<T>)store, tenantContext)
             : new TenantStoreWrapper<IStore<T>, T>(store, tenantContext);
    }
}
