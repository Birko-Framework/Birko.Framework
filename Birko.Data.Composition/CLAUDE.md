# Birko.Data.Composition

Runtime store decorator composition engine. Builds conditional decorator chains on `IAsyncBulkStore<T>` based on which interfaces the entity type implements.

## Components

### StoreWrapperBuilder
Static `Build<T>()` method that inspects `T` at runtime and wraps the store with applicable decorators.

**Decorator chain order (outermost to innermost):**
1. Tenant (if `T : ITenant`)
2. Default (if `T : IDefault`)
3. SoftDelete (if `T : ISoftDeletable`)
4. Audit (if `T : IAuditable`)
5. Timestamp (if `T : ITimestamped`)
6. Raw store

## Dependencies
- Birko.Data.Patterns (SoftDelete, Audit, Timestamp, Default wrappers)
- Birko.Data.Tenant (Tenant wrapper, ITenantContext)
- Birko.Data.Stores (IAsyncBulkStore<T>)
- Birko.Time.Abstractions (IDateTimeProvider)

## Namespace
`Birko.Data.Composition`
