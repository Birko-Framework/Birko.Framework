# Birko.Data.Composition

Runtime store decorator composition engine. Builds conditional decorator chains on `IAsyncBulkStore<T>` based on which interfaces the entity type implements.

## Components

### StoreWrapperBuilder
Static `Build<T>()` method that inspects `T` at runtime and wraps the store with applicable decorators.

**Decorator chain order (outermost to innermost):**
1. Tenant (if `T : ITenant` and `tenantContext` provided)
2. Default (if `T : IDefault`)
3. Sluggable (if `T : ISluggable`)
4. SoftDelete (if `T : ISoftDeletable`)
5. Audit (if `T : IAuditable` and `auditContext` provided)
6. Timestamp (if `T : ITimestamped`)
7. EventSourcing (if `T : IEventSourced` and `eventStore` provided)
8. Raw store

EventSourcing sits innermost so the recorded event payload includes Timestamp/Audit enrichments and so outer-wrapper rejections (slug collision, default conflict, tenant guard) do not leave orphan events behind. Soft-deletes emit an `"Updated"` event with `IsDeleted=true` — matches physical storage.

## Signature

```csharp
StoreWrapperBuilder.Build<T>(
    IAsyncBulkStore<T> rawStore,
    IDateTimeProvider? clock = null,
    IAuditContext? auditContext = null,
    ITenantContext? tenantContext = null,
    IAsyncEventStore? eventStore = null)
```

## Dependencies
- Birko.Data.Patterns (SoftDelete, Audit, Timestamp, Sluggable, Default wrappers)
- Birko.Data.Tenant (Tenant wrapper, ITenantContext)
- Birko.Data.EventSourcing (AsyncEventSourcingBulkStoreWrapper, IAsyncEventStore, IEventSourced)
- Birko.Data.Stores (IAsyncBulkStore<T>)
- Birko.Time.Abstractions (IDateTimeProvider)

Consumers must include all of the above `.projitems` files in their `.csproj`. The umbrella `Birko.Framework` already does so.

## Namespace
`Birko.Data.Composition`
