# Birko.Data.Composition

Composes Birko store decorator chains based on runtime type checks.

## Purpose

Birko provides individual store wrappers (Tenant, Default, Sluggable, SoftDelete, Audit, Timestamp, EventSourcing) in separate shared projects. Each wrapper has compile-time generic constraints (e.g., `T : ISoftDeletable`). This project provides `StoreWrapperBuilder` which builds the full decorator chain at runtime, applying only the wrappers whose constraints `T` satisfies.

## Dependencies

- **Birko.Data.Patterns** — SoftDelete, Audit, Timestamp, Sluggable, Default wrappers + IAuditContext, ISoftDeletable, IAuditable, ISluggable, IDefault, ITimestamped
- **Birko.Data.Tenant** — Tenant wrapper + ITenantContext, ITenant
- **Birko.Data.EventSourcing** — AsyncEventSourcingBulkStoreWrapper + IAsyncEventStore, IEventSourced
- **Birko.Data** — IAsyncBulkStore, AbstractModel
- **Birko.Time** — IDateTimeProvider

## Usage

```csharp
using Birko.Data.Composition;

// Build a decorated store — wrappers applied conditionally based on T's interfaces.
IAsyncBulkStore<Product> decoratedStore = StoreWrapperBuilder.Build(
    rawStore,
    clock: dateTimeProvider,        // optional, defaults to SystemDateTimeProvider
    auditContext: auditContext,     // optional, enables Audit wrapper if T : IAuditable
    tenantContext: tenantContext,   // optional, enables Tenant wrapper if T : ITenant
    eventStore: eventStore          // optional, enables EventSourcing wrapper if T : IEventSourced
);
```

## Decorator chain order

```
Outermost → Innermost:
Tenant → Default → Sluggable → SoftDelete → Audit → Timestamp → EventSourcing → RawStore
```

- **Tenant** (`T : ITenant`, `tenantContext`) — auto-filters reads by TenantGuid, auto-sets on create, guards update/delete
- **Default** (`T : IDefault`) — enforces only one entity with `IsDefault=true`, automatically unsets others on create/update
- **Sluggable** (`T : ISluggable`) — normalizes slug, enforces uniqueness over non-deleted records
- **SoftDelete** (`T : ISoftDeletable`) — converts Delete to soft-delete, filters deleted from reads
- **Audit** (`T : IAuditable`, `auditContext`) — auto-sets CreatedBy/UpdatedBy from IAuditContext
- **Timestamp** (`T : ITimestamped`) — auto-sets CreatedAt/UpdatedAt/PrevUpdatedAt
- **EventSourcing** (`T : IEventSourced`, `eventStore`) — appends Created/Updated/Deleted events to the event store on every write; reads pass through. Sits innermost so the recorded payload reflects Timestamp/Audit enrichments. Soft-deletes emit `"Updated"` events (IsDeleted=true) since SoftDelete converts the delete before EventSourcing sees it.

Each wrapper is only applied if `T` implements the required interface AND (where applicable) the corresponding context is provided.

## Related Projects

- [Birko.Data.Patterns](../Birko.Data.Patterns/) — Individual decorators
- [Birko.Data.Tenant](../Birko.Data.Tenant/) — Tenant store wrapper
- [Birko.Data.EventSourcing](../Birko.Data.EventSourcing/) — Event sourcing wrappers
