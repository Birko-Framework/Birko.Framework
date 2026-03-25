# Birko.Data.Patterns

Cross-cutting data patterns for the Birko Framework: Unit of Work, Soft Delete, Audit, and Paging.

## Features

- Unit of Work pattern with transaction management
- Soft Delete via decorator wrappers (sets `DeletedAt` instead of deleting)
- Audit tracking (automatic `CreatedBy`/`UpdatedBy` from context)
- Timestamp management (automatic `CreatedAt`/`UpdatedAt`/`PrevUpdatedAt` via `IDateTimeProvider`)
- Default constraint enforcement (only one entity with `IsDefault=true`)
- Paged results with navigation metadata
- All patterns available in sync, async, and bulk variants

## Installation

```bash
dotnet add package Birko.Data.Patterns
```

## Dependencies

- Birko.Data.Core (AbstractModel, interfaces)
- Birko.Data.Stores (store interfaces, wrappers)

## Usage

### Soft Delete

```csharp
// Wrap any store to add soft delete behavior
var store = new SoftDeleteStoreWrapper<Customer>(innerStore);
store.Delete(customer); // Sets DeletedAt instead of deleting

// Filter out deleted items
var expr = SoftDeleteFilter.CombineWithNotDeleted<Customer>(existingFilter);
```

### Audit Tracking

```csharp
var auditContext = new MyAuditContext(); // implements IAuditContext
var store = new AuditStoreWrapper<Customer>(innerStore, auditContext);
store.Create(customer); // Automatically sets CreatedBy
store.Update(customer); // Automatically sets UpdatedBy
```

### Timestamp Management

```csharp
var clock = new SystemDateTimeProvider(); // IDateTimeProvider from Birko.Time
var store = new AsyncTimestampStoreWrapper<Customer>(innerStore, clock);
store.Create(customer); // Automatically sets CreatedAt + UpdatedAt
store.Update(customer); // Shifts UpdatedAt to PrevUpdatedAt, sets new UpdatedAt
```

### Default Constraint

```csharp
// Wrap any bulk store to enforce only one entity can be the default
var store = new DefaultStoreWrapper<MyBulkStore, Currency>(innerStore);
store.Create(currency); // If currency.IsDefault=true, unsets all other defaults
store.Update(currency); // If currency.IsDefault=true, unsets all other defaults

// Async variant
var asyncStore = new AsyncDefaultStoreWrapper<MyAsyncBulkStore, Currency>(innerAsyncStore);
await asyncStore.CreateAsync(currency);
```

### Paging

```csharp
var result = new PagedResult<Customer>(items, totalCount: 100, page: 1, pageSize: 20);
// result.TotalPages, result.HasNextPage, result.HasPreviousPage
```

### Unit of Work

```csharp
var uow = GetUnitOfWork(); // IUnitOfWork
await uow.BeginAsync();
// ... multiple store operations ...
await uow.CommitAsync(); // or RollbackAsync()
```

## API Reference

### Unit of Work

- **IUnitOfWork** - `IsActive`, `BeginAsync()`, `CommitAsync()`, `RollbackAsync()`
- **IUnitOfWork\<TContext\>** - Generic variant for platform-specific contexts

### Soft Delete

- **ISoftDeletable** - Interface with `DeletedAt` (DateTime?)
- **SoftDeleteStoreWrapper\<T\>** / async/bulk variants

### Audit

- **IAuditable** - Interface with `CreatedBy`, `UpdatedBy` (Guid?)
- **IAuditContext** - Provides `CurrentUserId`
- **AuditStoreWrapper\<T\>** / async/bulk variants

### Timestamp

- **ITimestamped** (in Birko.Data.Core) - Interface with `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt`
- **TimestampStoreWrapper\<T\>** / async/bulk variants

### Paging

- **PagedResult\<T\>** - `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`

### Index Management

Provider-agnostic abstractions for managing database indexes across all providers (SQL, MongoDB, RavenDB, ElasticSearch).

```csharp
// Uniform interface — works with any provider
IIndexManager indexManager = GetProviderIndexManager();

// Create an index
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "idx_user_email",
    Fields = new[] { IndexField.Ascending("Email") },
    Unique = true
}, scope: "Users"); // scope = table/collection name

// List all indexes
var indexes = await indexManager.ListAsync(scope: "Users");

// Check existence / get info / drop
bool exists = await indexManager.ExistsAsync("idx_user_email", scope: "Users");
IndexInfo? info = await indexManager.GetInfoAsync("idx_user_email", scope: "Users");
await indexManager.DropAsync("idx_user_email", scope: "Users");
```

**Models:**
- **IndexDefinition** — Name, Fields, Unique, Sparse, ExpireAfter, Properties
- **IndexField** — Name, IsDescending (bool), FieldType (Standard/Text/Geo2d/Geo2dSphere/Hashed)
  - Static factories: `Ascending()`, `Descending()`, `Text()`, `Hashed()`, `Geo2dSphere()`
- **IndexInfo** — Name, Fields, Unique, Sparse, ExpireAfter, SizeInBytes, State, Properties
- **IndexManagementException** — Typed exception with IndexName + Scope

**Provider implementations:**
- `SqlIndexManager` (base) + `PostgreSqlIndexManager`, `MSSqlIndexManager`, `SqLiteIndexManager`, `MySqlIndexManager`
- `MongoDBIndexManager` — full CRUD + TTL, text, compound, geo indexes
- `RavenDBIndexManager` — full CRUD + reset, enable/disable, priority, stale detection
- `ElasticSearchIndexManagerAdapter` — wraps existing IndexManager, exposes `.Native` for ES-specific features

### Specification

- **ISpecification\<T\>** — `IsSatisfiedBy(T)`, composable with `And()`, `Or()`, `Not()`
- **RuleSpecification\<T\>** — Bridges Birko.Rules `IRule` to the Specification pattern

### Default Constraint

- **IDefault** (in Birko.Contracts) — Interface with `IsDefault` (bool)
- **DefaultStoreWrapper\<T\>** — Wraps `IBulkStore<T>`, enforces single default. Requires bulk store
- **AsyncDefaultStoreWrapper\<T\>** — Async variant wrapping `IAsyncBulkStore<T>`

### Concurrency

- **IVersioned** — Interface with `Version` (int) for optimistic concurrency
- **VersionedStoreWrapper\<T\>** / async — Checks version on update, throws `ConcurrentUpdateException`

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces

## License

Part of the Birko Framework.
