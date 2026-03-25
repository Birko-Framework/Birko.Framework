using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using Birko.Time;

namespace Birko.Data.Composition;

/// <summary>
/// Builds a store decorator chain based on which interfaces T implements.
/// Chain order (outermost → innermost): Tenant → Default → SoftDelete → Audit → Timestamp → RawStore.
/// Uses runtime type checks because C# generic constraints are compile-time only.
/// </summary>
public static class StoreWrapperBuilder
{
    /// <summary>
    /// Wraps a raw IAsyncBulkStore with the appropriate decorators for T.
    /// Each decorator is applied only if T implements the required interface
    /// and the corresponding context parameter is provided (where needed).
    /// </summary>
    public static IAsyncBulkStore<T> Build<T>(
        IAsyncBulkStore<T> rawStore,
        IDateTimeProvider? clock = null,
        IAuditContext? auditContext = null,
        ITenantContext? tenantContext = null)
        where T : AbstractModel, new()
    {
        var effectiveClock = clock ?? new SystemDateTimeProvider();
        IAsyncBulkStore<T> store = rawStore;

        // Innermost: Timestamp (applies to all ITimestamped entities — AbstractLogModel+)
        if (typeof(ITimestamped).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncTimestampBulkStoreWrapper<,>), store, effectiveClock);
        }

        // Audit: sets CreatedBy/UpdatedBy (applies to IAuditable entities)
        if (auditContext is not null && typeof(IAuditable).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncAuditBulkStoreWrapper<,>), store, auditContext);
        }

        // SoftDelete: filters deleted on reads, converts delete to update (applies to ISoftDeletable entities)
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncSoftDeleteBulkStoreWrapper<,>), store, effectiveClock);
        }

        // Default: enforces single IsDefault=true (applies to IDefault entities)
        if (typeof(IDefault).IsAssignableFrom(typeof(T)))
        {
            store = WrapSingle(typeof(AsyncDefaultStoreWrapper<,>), store);
        }

        // Outermost: Tenant filter (applies to ITenant entities)
        if (tenantContext is not null && typeof(ITenant).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncTenantBulkStoreWrapper<,>), store, tenantContext);
        }

        return store;
    }

    private static IAsyncBulkStore<T> Wrap<T>(Type wrapperType, IAsyncBulkStore<T> store, params object[] args)
        where T : AbstractModel, new()
    {
        var closed = wrapperType.MakeGenericType(typeof(IAsyncBulkStore<T>), typeof(T));
        var ctorArgs = new object[args.Length + 1];
        ctorArgs[0] = store;
        Array.Copy(args, 0, ctorArgs, 1, args.Length);
        return (IAsyncBulkStore<T>)Activator.CreateInstance(closed, ctorArgs)!;
    }

    private static IAsyncBulkStore<T> WrapSingle<T>(Type wrapperType, IAsyncBulkStore<T> store)
        where T : AbstractModel, new()
    {
        var closed = wrapperType.MakeGenericType(typeof(IAsyncBulkStore<T>), typeof(T));
        return (IAsyncBulkStore<T>)Activator.CreateInstance(closed, store)!;
    }
}
