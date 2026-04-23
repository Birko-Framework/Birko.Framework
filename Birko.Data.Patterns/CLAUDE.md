# Birko.Data.Patterns

## Overview
Cross-cutting data patterns including Unit of Work, soft delete, audit tracking, and paging.

## Project Location
`C:\Source\Birko.Data.Patterns\`

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

### Audit
- `IAuditable` - Interface with `CreatedBy`, `UpdatedBy` properties (Guid?)
- `IAuditContext` - Interface providing `CurrentUserId`
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
- `ISluggable` - Interface with `Slug` property (string?) and `GetSlugSource()` method
- `SlugGenerator` - Static utility for slug normalization (lowercase, diacritics removal, hyphen delimiters) and uniqueness checking with numeric suffixes (-1, -2, etc.)
- `SluggableStoreWrapper<T>` - Wraps `IStore<T>`, auto-generates slug from `GetSlugSource()` on create/update, ensures uniqueness
- `SluggableBulkStoreWrapper<T>` - Bulk store variant with internal collision tracking within batch creates
- `AsyncSluggableStoreWrapper<T>` - Async store variant
- `AsyncSluggableBulkStoreWrapper<T>` - Async bulk store variant

### Default Constraint
- `IDefault` - Interface in Birko.Contracts with `IsDefault` property (bool)
- `DefaultStoreWrapper<TStore, T>` - Wraps `IBulkStore<T>`, enforces only one entity has `IsDefault=true`. On create/update, automatically unsets other defaults. Implements `IBulkStore<T>`
- `AsyncDefaultStoreWrapper<TStore, T>` - Async variant wrapping `IAsyncBulkStore<T>`, implements `IAsyncBulkStore<T>`
- Note: Requires bulk store (not plain IStore) because enforcing the constraint requires bulk read + update

### Schema (Birko.Data.Patterns.Schema)
Provider-agnostic field and schema abstractions. Used by both the migration system and the SQL model mapping framework.
- `FieldType` - Enum: String, Integer, Long, Decimal, Double, Boolean, DateTime, Guid, Binary, Json
- `FieldDescriptor` - Mutable field metadata: Name, Type, ColumnName, IsPrimary, IsUnique, IsRequired, IsIgnored, MaxLength, Precision, Scale, IsAutoIncrement, DefaultValue, IndexName, IndexOrder, IndexDescending. Used by migrations (ISchemaBuilder.AddField) and SQL mapping (FieldBuilder<T>).
- `ISchemaBuilder` - CreateCollection, DropCollection, CollectionExists, CreateIndex, DropIndex, AddField, DropField, RenameField
- `ICollectionBuilder` - Fluent: WithField(name, type) and WithField(FieldDescriptor)
- `IIndexBuilder` - Fluent: WithField (uses IndexFieldType from IndexManagement), Unique, Sparse, WithProperty

### Specification
- `ISpecification<T>` - Interface with `IsSatisfiedBy(entity)` and `ToExpression()` for store filtering
- `Specification<T>` - Abstract base class with cached compiled expression and `And()`, `Or()`, `Not()` methods + `&`, `|`, `!` operators
- `AndSpecification<T>` - Combines two specifications with logical AND
- `OrSpecification<T>` - Combines two specifications with logical OR
- `NotSpecification<T>` - Negates a specification

### Concurrency
- `IVersioned` - Interface with `Version` property (long) for optimistic concurrency
- `ConcurrentUpdateException` - Thrown on version mismatch, includes `EntityType`, `EntityId`, `ExpectedVersion`
- `VersionedStoreWrapper<T>` - Wraps `IStore<T>`, sets Version=1 on create, increments+checks on update
- `AsyncVersionedStoreWrapper<T>` - Async variant wrapping `IAsyncStore<T>`

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
