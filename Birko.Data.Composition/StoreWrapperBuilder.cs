using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
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
/// Chain order (outermost → innermost): Default → Sluggable → SoftDelete → Tenant → Audit → Timestamp → EventSourcing → RawStore.
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
        ITenantContext? tenantContext = null,
        IAsyncEventStore? eventStore = null,
        TenantIsolationMode tenantMode = TenantIsolationMode.Permissive,
        Func<IAsyncBulkStore<T>, ITenantContext, IAsyncBulkStore<T>>? tenantWrapperFactory = null)
        where T : AbstractModel, new()
    {
        var effectiveClock = clock ?? new SystemDateTimeProvider();
        IAsyncBulkStore<T> store = rawStore;

        // Innermost: EventSourcing (applies to IEventSourced entities when an event store is provided).
        // Sits closest to raw so the recorded payload reflects the entity after all outer enrichments
        // (Timestamp, Audit) and so outer-wrapper rejections (slug collision, default conflict, tenant
        // guard) do not leave orphan events behind. Soft-deletes emit an "Updated" event with
        // IsDeleted=true — matches physical storage.
        if (eventStore is not null && typeof(IEventSourced).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncEventSourcingBulkStoreWrapper<,>), store, eventStore, null, effectiveClock);
        }

        // Timestamp (applies to all ITimestamped entities — AbstractLogModel+)
        if (typeof(ITimestamped).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncTimestampBulkStoreWrapper<,>), store, effectiveClock);
        }

        // Audit: sets CreatedBy/UpdatedBy (applies to IAuditable entities)
        if (auditContext is not null && typeof(IAuditable).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncAuditBulkStoreWrapper<,>), store, auditContext);
        }

        // Tenant filter (applies to ITenant entities).
        // Positioned INSIDE the uniqueness/soft-delete wrappers (Default, Sluggable, SoftDelete) but
        // OUTSIDE the enrichers (Audit, Timestamp, EventSourcing). Rationale:
        //  - Default/Sluggable issue their OWN probe + corrective queries against their inner store;
        //    with Tenant inside them, those queries pass through the tenant filter and are scoped to
        //    the current tenant — so uniqueness (single IsDefault, unique slug) is enforced PER TENANT,
        //    not globally across tenants (STORY-045: Tenant-outermost silently corrupted sibling tenants).
        //  - Still outside EventSourcing/Timestamp/Audit so the tenant guard rejects a cross-tenant write
        //    BEFORE an event is recorded (no orphan events) and TenantGuid is stamped in time to be
        //    captured in the audit/event payload.
        // A consumer can inject a custom fail-closed wrapper via tenantWrapperFactory; otherwise the
        // built-in wrapper is constructed with tenantMode (STORY-044 — Permissive keeps the fail-open
        // default; Strict throws when no tenant is in scope).
        if (tenantContext is not null && typeof(ITenant).IsAssignableFrom(typeof(T)))
        {
            store = tenantWrapperFactory is not null
                ? tenantWrapperFactory(store, tenantContext)
                : Wrap(typeof(AsyncTenantBulkStoreWrapper<,>), store, tenantContext, tenantMode);
        }

        // SoftDelete: filters deleted on reads, converts delete to update (applies to ISoftDeletable entities)
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            store = Wrap(typeof(AsyncSoftDeleteBulkStoreWrapper<,>), store, effectiveClock);
        }

        // Sluggable: normalizes slug and ensures uniqueness (applies to ISluggable entities)
        // Positioned after SoftDelete so uniqueness checks only consider non-deleted records.
        if (typeof(ISluggable).IsAssignableFrom(typeof(T)))
        {
            store = WrapSingle(typeof(AsyncSluggableBulkStoreWrapper<,>), store);
        }

        // Outermost: Default — enforces single IsDefault=true (applies to IDefault entities)
        if (typeof(IDefault).IsAssignableFrom(typeof(T)))
        {
            store = WrapSingle(typeof(AsyncDefaultStoreWrapper<,>), store);
        }

        return store;
    }

    private static IAsyncBulkStore<T> Wrap<T>(Type wrapperType, IAsyncBulkStore<T> store, params object?[] args)
        where T : AbstractModel, new()
    {
        var closed = wrapperType.MakeGenericType(typeof(IAsyncBulkStore<T>), typeof(T));
        var ctorArgs = new object?[args.Length + 1];
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
