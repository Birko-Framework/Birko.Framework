# Birko.Data.Patterns

## Overview
Cross-cutting data patterns including Unit of Work, soft delete, audit tracking, and paging.

## Project Location
`Birko.Data.Patterns/`

## Components

### Unit of Work
- `IUnitOfWork` - Interface with `IsActive`, `BeginAsync()`, `CommitAsync()`, `RollbackAsync()`
- `IUnitOfWork<TContext>` - Generic variant for platform-specific contexts
- `UnitOfWorkException` - Base exception
- `NoActiveTransactionException` - Thrown when operating without active transaction
- `TransactionAlreadyActiveException` - Thrown when beginning duplicate transaction

### Soft Delete
- `ISoftDeletable` - Interface with `DeletedAt` property (DateTime?)
- `SoftDeleteFilter` - Static utility with `CombineWithNotDeleted<T>()` expression combiner
- `SoftDeleteStoreWrapper<T>` - Wraps `IStore<T>`, intercepts `Delete()` to set `DeletedAt`
- `SoftDeleteBulkStoreWrapper<T>` - Bulk store variant
- `AsyncSoftDeleteStoreWrapper<T>` - Async store variant
- `AsyncSoftDeleteBulkStoreWrapper<T>` - Async bulk store variant
- A soft-deleted row is NOT FOUND by an update (`EntityNotFoundException`; bulk writes the live items first), an
  already-deleted row keeps its `DeletedAt`, and a filtered write checks the caller's filter (null / reducing-to-all
  refused) before the not-deleted clause is added (TASK-159)
- Every delete path retires a default in the same write (`SoftDeleteFilter.Stamp` / `Deletion`): `IsDefault = false`,
  `DefaultMarker = null`. A kept marker refused every later promotion under the unique index (TASK-638)
- `Save` of a soft-deleted entity throws `EntitySoftDeletedException` (a `StoreException`, never an
  `EntityNotFoundException`, which `StoreSave` would turn into a create); `Restore` / `RestoreAsync` clears `DeletedAt`
  and writes the entity. A bulk update names its soft-deleted and its missing items in one `EntityNotFoundException`
  (TASK-649)
- `ISoftDeleteStoreWrapper` marks the four wrappers; the Sluggable probe asks beneath one directly inside it, because a
  soft-deleted row keeps its slug until it is removed for good (TASK-638)

### Audit
- `IAuditable` - Interface with `CreatedBy`, `UpdatedBy` properties (Guid?)
- `IAuditContext` - Interface providing `CurrentUserGuid`
- `AuditStoreWrapper<T>` - Wraps `IStore<T>`, sets `CreatedBy`/`UpdatedBy` from `IAuditContext`
- `AuditBulkStoreWrapper<T>` - Bulk store variant
- `AsyncAuditStoreWrapper<T>` - Async store variant
- `AsyncAuditBulkStoreWrapper<T>` - Async bulk store variant

### Timestamp
- `ITimestamped` - Interface in Birko.Data.Core with `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt` (AbstractLogModel implements it)
- `TimestampStoreWrapper<T>` - Wraps `IStore<T>`, auto-sets timestamps using `IDateTimeProvider` from Birko.Time
- `TimestampBulkStoreWrapper<T>` - Bulk store variant
- `AsyncTimestampStoreWrapper<T>` - Async store variant
- `AsyncTimestampBulkStoreWrapper<T>` - Async bulk store variant

### Paging
- `PagedResult<T>` - Sealed class with `Items`, `TotalCount`, `Page`, `PageSize`
  - Computed: `TotalPages`, `HasNextPage`, `HasPreviousPage`
  - Static: `Empty()` factory method
- `IPagedRepository<T>` - Sync interface with `ReadPaged(filter, orderBy, page, pageSize)`
- `IAsyncPagedRepository<T>` - Async interface with `ReadPagedAsync(filter, orderBy, page, pageSize, ct)`
- `PagedRepositoryWrapper<T>` - Wraps `IBulkRepository<T>`, combines `Read()` + `Count()` into `PagedResult<T>`
- `AsyncPagedRepositoryWrapper<T>` - Wraps `IAsyncBulkRepository<T>`, runs Read and Count in parallel

### Sluggable
- `ISluggable` - Interface with `Slug` property (string?), `GetSlugSource()` method and `IsReservedSlug(slug)` (default `false`; a reserved slug is de-duplicated like a taken one — e.g. a literal route segment beside `{slug}`, TASK-505)
- `SlugGenerator` - Static utility for slug normalization (lowercase, diacritics removal, hyphen delimiters) and uniqueness checking with numeric suffixes (-2, -3, etc.)
- `SluggableStoreWrapper<T>` - Wraps `IStore<T>`, auto-generates slug from `GetSlugSource()` on create/update, ensures uniqueness
- `SluggableBulkStoreWrapper<T>` - Bulk store variant with internal collision tracking within batch creates
- `AsyncSluggableStoreWrapper<T>` - Async store variant
- `AsyncSluggableBulkStoreWrapper<T>` - Async bulk store variant

### Default Constraint
- `IDefault` - Interface in Birko.Contracts with `IsDefault` property (bool)
- `DefaultStoreWrapper<TStore, T>` - Wraps `IBulkStore<T>`, enforces only one entity has `IsDefault=true`. On create/update, automatically unsets other defaults. Implements `IBulkStore<T>`
- `AsyncDefaultStoreWrapper<TStore, T>` - Async variant wrapping `IAsyncBulkStore<T>`, implements `IAsyncBulkStore<T>`
- Note: Requires bulk store (not plain IStore) because enforcing the constraint requires bulk read + update
- Filtered updates (an `Action<T>`, or a `PropertyUpdate` that writes `IsDefault`) read, apply, keep the last default and
  write once, on both paths (TASK-159)
- Concurrent promotions (TASK-632): a model implementing `IDefaultMarker` (`bool? DefaultMarker`, kept `true`/null, never
  `false`) under a unique index is refused a second default with `StoreUniqueViolationException`; the wrapper unsets
  the winner and writes again (last writer wins, `MaxDefaultAttempts` = 5). Batches write demotions before the promotion.
  Without the marker, or on a store with no unique index, the race is only narrowed
- Slug wrappers retry the same way on a unique index over the slug (next suffix, `MaxSlugAttempts` = 5); a refusal for
  another key surfaces; a batch create retries only while none of it is stored
- Inside a caller's transaction (TASK-685): the probe that decides a retry runs through
  `StoreException.ProbeAfterRefusal(Async)`. If it fails, the caller gets the typed `StoreUniqueViolationException`
  naming the failure, never the driver's error (PostgreSQL's aborted transaction answers `25P02`). Cancellation passes
  through. The sync twins probe in the catch body, not in an exception filter, which would swallow the failure.
  Per provider: SQL Server retries in place; MySQL's REPEATABLE READ re-probe misses the other commit, so the refusal
  stands; PostgreSQL refuses typed. Savepoints that would let PostgreSQL retry are TASK-694

### Schema (Birko.Data.Patterns.Schema)
Provider-agnostic field and schema abstractions. Used by both the migration system and the SQL model mapping framework.
- `FieldType` - Enum: String, Integer, Long, Decimal, Double, Boolean, DateTime, Guid, Binary, Json
- `FieldDescriptor` - Mutable field metadata: Name, Type, ColumnName, IsPrimary, IsUnique, IsRequired, IsIgnored, MaxLength, Precision, Scale, IsAutoIncrement, DefaultValue, IndexName, IndexOrder, IndexDescending. Used by migrations (ISchemaBuilder.AddField) and SQL mapping (FieldBuilder<T>).
- `ISchemaBuilder` - CreateCollection, DropCollection, CollectionExists, CreateIndex, DropIndex, AddField, DropField, RenameField
- `ICollectionBuilder` - Fluent: WithField(name, type) and WithField(FieldDescriptor)
- `IIndexBuilder` - Fluent: WithField (uses IndexFieldType from IndexManagement), Unique, Sparse, WithProperty
- `SchemaBuildLedger` - The chains a schema builder handed out; `ISchemaBuilder.PendingBuilds` lists the unbuilt ones and every migration runner fails on them (TASK-627)

### Index management (Birko.Data.Patterns.IndexManagement)
- `IIndexManager`, `IndexDefinition`, `IndexInfo`, `IndexManagementException` - the provider-neutral index API
- `IndexDefinition.WhereNotNull` / `WhereNull` - a partial index, the two closed polarities SQL declares (TASK-636):
  MongoDB renders and reads them back, SQL carries them as predicates, RavenDB / Cosmos DB / ElasticSearch refuse them
  (`RefusePredicates`). `IndexInfo`'s lists are `null` when a manager does not report predicates
- `IndexDeclarationDriftDetector.DetectAsync(manager, declared, scope)` - compares declared `IndexDefinition`s with the manager's `GetInfoAsync`: `Missing`, `ColumnsDiffer`, `UniquenessDiffers`, `PredicateDiffers`, and `Unverified` for an index it could not compare (never counted clean). The declared side for document stores, which declare no indexes on the model (TASK-635); SQL has its own model-derived check (TASK-622)

### Specification
- `ISpecification<T>` - Interface with `IsSatisfiedBy(entity)` and `ToExpression()` for store filtering
- `Specification<T>` - Abstract base class with cached compiled expression and `And()`, `Or()`, `Not()` methods + `&`, `|`, `!` operators
- `AndSpecification<T>` - Combines two specifications with logical AND
- `OrSpecification<T>` - Combines two specifications with logical OR
- `NotSpecification<T>` - Negates a specification

### Concurrency
- `IVersioned` - Interface with `Version` property (long) for optimistic concurrency
- `ConcurrentUpdateException` - Thrown on version mismatch, includes `EntityType`, `EntityGuid`, `ExpectedVersion`
- `VersionedStoreWrapper<T>` - Wraps `IStore<T>`, sets Version=1 on create, increments+checks on update
- `AsyncVersionedStoreWrapper<T>` - Async variant wrapping `IAsyncStore<T>`
- A missing row is `EntityNotFoundException` (so `Save` of an unstored Guid creates), and a failed inner write restores
  `Version` before rethrowing (TASK-159)
- Decorators that add an assignment to a caller's `PropertyUpdate` stamp `updates.Clone()`, never the caller's instance

## Architecture

### Decorator Pattern
Store wrappers use the decorator pattern to add cross-cutting concerns:
```
IBulkStore<T> -> DefaultStoreWrapper<T> -> SoftDeleteStoreWrapper<T> -> SluggableStoreWrapper<T> -> TimestampStoreWrapper<T> -> AuditStoreWrapper<T> -> actual store
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories, Birko.Time (for IDateTimeProvider in Timestamp/SoftDelete wrappers)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
