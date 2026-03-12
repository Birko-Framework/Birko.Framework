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

### Paging
- `PagedResult<T>` - Sealed class with `Items`, `TotalCount`, `Page`, `PageSize`
  - Computed: `TotalPages`, `HasNextPage`, `HasPreviousPage`
  - Static: `Empty()` factory method
- `IPagedRepository<T>` - Sync interface with `ReadPaged(filter, orderBy, page, pageSize)`
- `IAsyncPagedRepository<T>` - Async interface with `ReadPagedAsync(filter, orderBy, page, pageSize, ct)`
- `PagedRepositoryWrapper<T>` - Wraps `IBulkRepository<T>`, combines `Read()` + `Count()` into `PagedResult<T>`
- `AsyncPagedRepositoryWrapper<T>` - Wraps `IAsyncBulkRepository<T>`, runs Read and Count in parallel

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
IStore<T> -> SoftDeleteStoreWrapper<T> -> AuditStoreWrapper<T> -> actual store
```

## Dependencies
- Birko.Data

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
