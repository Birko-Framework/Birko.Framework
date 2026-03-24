# Birko.Data.Composition

Composes Birko store decorator chains based on runtime type checks.

## Purpose

Birko provides individual store wrappers (Tenant, SoftDelete, Audit, Timestamp) in separate shared projects. Each wrapper has compile-time generic constraints (e.g., `T : ISoftDeletable`). This project provides `StoreWrapperBuilder` which builds the full decorator chain at runtime, applying only the wrappers whose constraints `T` satisfies.

## Dependencies

- **Birko.Data.Patterns** — SoftDelete, Audit, Timestamp wrappers + IAuditContext, ISoftDeletable, IAuditable
- **Birko.Data.Tenant** — Tenant wrapper + ITenantContext, ITenant
- **Birko.Data** — IAsyncBulkStore, AbstractModel
- **Birko.Time** — IDateTimeProvider

## Usage

```csharp
using Birko.Data.Composition;

// Build a decorated store — wrappers applied conditionally based on T's interfaces
IAsyncBulkStore<Product> decoratedStore = StoreWrapperBuilder.Build(
    rawStore,
    clock: dateTimeProvider,        // optional, defaults to SystemDateTimeProvider
    auditContext: auditContext,      // optional, enables Audit wrapper if T : IAuditable
    tenantContext: tenantContext     // optional, enables Tenant wrapper if T : ITenant
);
```

## Decorator chain order

```
Outermost → Innermost:
Tenant → SoftDelete → Audit → Timestamp → RawStore
```

- **Tenant** (`T : ITenant`) — auto-filters reads by TenantGuid, auto-sets on create, guards update/delete
- **SoftDelete** (`T : ISoftDeletable`) — converts Delete to soft-delete, filters deleted from reads
- **Audit** (`T : IAuditable`) — auto-sets CreatedBy/UpdatedBy from IAuditContext
- **Timestamp** (`T : ITimestamped`) — auto-sets CreatedAt/UpdatedAt/PrevUpdatedAt

Each wrapper is only applied if `T` implements the required interface AND the corresponding context is provided.

## Related Projects

- [Birko.Data.Patterns](../Birko.Data.Patterns/) — Individual decorators
- [Birko.Data.Tenant](../Birko.Data.Tenant/) — Tenant store wrapper
