# Birko.Data.Patterns

Cross-cutting data patterns for the Birko Framework: Unit of Work, Soft Delete, Audit, and Paging.

## Features

- Unit of Work pattern with transaction management
- Soft Delete via decorator wrappers (sets `DeletedAt` instead of deleting)
- Audit tracking (automatic `CreatedBy`/`UpdatedBy` from context)
- Paged results with navigation metadata
- All patterns available in sync, async, and bulk variants

## Installation

```bash
dotnet add package Birko.Data.Patterns
```

## Dependencies

- Birko.Data

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

### Paging

- **PagedResult\<T\>** - `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`

## Related Projects

- [Birko.Data](../Birko.Data/) - Core interfaces

## License

Part of the Birko Framework.
