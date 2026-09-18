using Birko.Data.Stores;
using Birko.Data.Tenant.Models;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// Extension methods for creating tenant-aware stores
/// </summary>
public static class TenantStoreExtensions
{
    /// <summary>
    /// Wrap a store with tenant filtering. Pass <paramref name="mode"/> = <see cref="TenantIsolationMode.Strict"/>
    /// for fail-closed isolation (throws instead of returning cross-tenant data when no tenant is in scope).
    /// </summary>
    public static IAsyncStore<T> AsTenantAware<T>(this IAsyncStore<T> store, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive)
        where T : Data.Models.AbstractModel, ITenant
    {
        return (store is IAsyncBulkStore<T>)
             ? new AsyncTenantBulkStoreWrapper<IAsyncBulkStore<T>, T>((IAsyncBulkStore<T>)store, tenantContext, mode)
             : new AsyncTenantStoreWrapper<IAsyncStore<T>, T>(store, tenantContext, mode);
    }

    /// <summary>
    /// Wrap a store with tenant filtering. Pass <paramref name="mode"/> = <see cref="TenantIsolationMode.Strict"/>
    /// for fail-closed isolation (throws instead of returning cross-tenant data when no tenant is in scope).
    /// </summary>
    public static IStore<T> AsTenantAware<T>(this IStore<T> store, ITenantContext? tenantContext = null, TenantIsolationMode mode = TenantIsolationMode.Permissive)
        where T : Data.Models.AbstractModel, ITenant
    {
        return (store is IBulkStore<T>)
             ? new TenantBulkStoreWrapper<IBulkStore<T>, T>((IBulkStore<T>)store, tenantContext, mode)
             : new TenantStoreWrapper<IStore<T>, T>(store, tenantContext, mode);
    }
}
