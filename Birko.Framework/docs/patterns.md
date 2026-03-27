# Data Patterns Guide

## Overview

Birko.Data.Patterns provides cross-cutting data access patterns implemented as decorator wrappers around existing stores and repositories: Unit of Work, Soft Delete, Audit, Default Constraint, Paging, Specifications, and Optimistic Concurrency.

## Unit of Work

Batches multiple store operations into a single atomic transaction.

### Interface

```csharp
public interface IUnitOfWork : IAsyncDisposable
{
    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IUnitOfWork<TContext> : IUnitOfWork
{
    TContext Context { get; }
}
```

### Exceptions

- `NoActiveTransactionException` — commit/rollback called without begin
- `TransactionAlreadyActiveException` — begin called while already active
- `UnitOfWorkException` — general UoW failures

Platform-specific implementations live in provider projects (e.g., `Birko.Data.SQL/UnitOfWork/`).

## Soft Delete

Marks entities as deleted instead of physically removing them.

### Model Interface

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
```

### Store Wrappers

```csharp
// Wrap any store to auto-filter deleted records and soft-delete on Delete()
var store = new AsyncDataBaseBulkStore<PostgreSQLConnector, Product>();
var softStore = new AsyncSoftDeleteStoreWrapper<Product>(store);

// Delete sets DeletedAt instead of removing:
await softStore.DeleteAsync(product);  // product.DeletedAt = DateTime.UtcNow

// All reads automatically filter out soft-deleted records:
var active = await softStore.ReadAsync();  // Only non-deleted items

// Filter-based bulk operations respect soft-delete:
await softStore.UpdateAsync(x => x.Price > 100,
    new PropertyUpdate<Product>().Set(x => x.Active, false));  // Only updates non-deleted
await softStore.DeleteAsync(x => x.IsExpired);                  // Soft-deletes matching non-deleted
```

### Expression Helper

```csharp
// Combine custom filter with not-deleted filter:
var filter = SoftDeleteFilter.CombineWithNotDeleted<Product>(p => p.Price > 100);
```

## Audit

Automatically tracks who created and modified entities.

### Model Interface

```csharp
public interface IAuditable
{
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
}
```

### Audit Context

```csharp
public interface IAuditContext
{
    Guid? CurrentUserId { get; }
}
```

### Store Wrappers

```csharp
var auditContext = new MyAuditContext(currentUserId);
var store = new AsyncDataBaseBulkStore<MSSqlConnector, Order>();
var auditedStore = new AsyncAuditStoreWrapper<Order>(store, auditContext);

// Create auto-sets CreatedBy:
await auditedStore.CreateAsync(order);  // order.CreatedBy = currentUserId

// Update auto-sets UpdatedBy:
await auditedStore.UpdateAsync(order);  // order.UpdatedBy = currentUserId
```

## Timestamp

Automatically manages `CreatedAt`, `UpdatedAt`, and `PrevUpdatedAt` timestamps on store operations.

### Model Interface

```csharp
// In Birko.Data.Core (Birko.Data.Models namespace)
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
    DateTime? PrevUpdatedAt { get; set; }
}
```

`AbstractLogModel` implements `ITimestamped`, so all existing log models work automatically.

### Store Wrappers

```csharp
var clock = new SystemDateTimeProvider(); // or any IDateTimeProvider
var store = new AsyncDataBaseBulkStore<PostgreSQLConnector, Order>();
var timestampedStore = new AsyncTimestampBulkStoreWrapper<Order>(store, clock);

// Create sets CreatedAt + UpdatedAt to UtcNow, PrevUpdatedAt to null:
await timestampedStore.CreateAsync(order);

// Update shifts UpdatedAt into PrevUpdatedAt, sets new UpdatedAt:
await timestampedStore.UpdateAsync(order);
```

### Stacking with Other Decorators

```csharp
// Stack: versioning -> default -> audit -> timestamp -> soft delete -> base store
var store = new AsyncVersionedStoreWrapper<Order>(
    new AsyncDefaultStoreWrapper<IAsyncBulkStore<Order>, Order>(
        new AsyncAuditStoreWrapper<Order>(
            new AsyncTimestampStoreWrapper<Order>(
                new AsyncSoftDeleteStoreWrapper<Order>(baseStore, clock),
                clock),
            auditContext)));
```

## Default Constraint

Enforces that only one entity in a store can have `IsDefault = true` at a time.

### Interface

Uses `IDefault` from `Birko.Contracts`:

```csharp
public interface IDefault
{
    bool IsDefault { get; set; }
}
```

### Usage

```csharp
// Wrap a bulk store to enforce single-default constraint
var store = new AsyncDefaultStoreWrapper<IAsyncBulkStore<Currency>, Currency>(baseStore);

// Creating with IsDefault=true automatically unsets all other defaults:
await store.CreateAsync(newCurrency);

// Updating with IsDefault=true does the same:
await store.UpdateAsync(existingCurrency);
```

> **Note:** Requires a bulk store (`IBulkStore<T>` / `IAsyncBulkStore<T>`) because enforcing the constraint needs bulk read and update to find and unset other defaults.

### Variants

- `DefaultStoreWrapper<TStore, T>` — Sync, implements `IBulkStore<T>`
- `AsyncDefaultStoreWrapper<TStore, T>` — Async, implements `IAsyncBulkStore<T>`

## Paging

Paginated queries with total count for UI pagination.

### PagedResult

```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; }
    public long TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages { get; }     // Computed
    public bool HasNextPage { get; }   // Computed
    public bool HasPreviousPage { get; } // Computed
}
```

### Repository Interfaces

```csharp
public interface IPagedRepository<T> where T : AbstractModel
{
    PagedResult<T> ReadPaged(int page, int pageSize,
                              Expression<Func<T, bool>>? filter = null,
                              OrderBy<T>? orderBy = null);
}

public interface IAsyncPagedRepository<T> where T : AbstractModel
{
    Task<PagedResult<T>> ReadPagedAsync(int page, int pageSize,
                                         Expression<Func<T, bool>>? filter = null,
                                         OrderBy<T>? orderBy = null,
                                         CancellationToken ct = default);
}
```

### Wrappers

```csharp
var repo = new AsyncDataBaseRepository<Product, PostgreSQLConnector>();
var pagedRepo = new AsyncPagedRepositoryWrapper<Product>(repo);

var result = await pagedRepo.ReadPagedAsync(
    page: 1,
    pageSize: 20,
    filter: p => p.Category == "Electronics",
    orderBy: new OrderBy<Product>(p => p.Name));

// result.Items, result.TotalCount, result.TotalPages, etc.
```

Async paging runs Read and Count in parallel for performance.

## Specifications

Composable, reusable query predicates.

### Interface

```csharp
public interface ISpecification<T>
{
    bool IsSatisfiedBy(T entity);
    Expression<Func<T, bool>> ToExpression();
}
```

### Usage

```csharp
public class ActiveProductSpec : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => p => p.IsActive && p.DeletedAt == null;
}

public class PriceRangeSpec : Specification<Product>
{
    private readonly decimal _min, _max;
    public PriceRangeSpec(decimal min, decimal max) { _min = min; _max = max; }

    public override Expression<Func<Product, bool>> ToExpression()
        => p => p.Price >= _min && p.Price <= _max;
}

// Compose with operators:
var spec = new ActiveProductSpec() & new PriceRangeSpec(10, 100);
// Or:
var spec = new ActiveProductSpec() | new PriceRangeSpec(0, 5);
// Negate:
var spec = !new ActiveProductSpec();

// Use with stores:
var products = await store.ReadAsync(spec.ToExpression());
```

## Optimistic Concurrency

Prevents lost updates in concurrent scenarios.

### Model Interface

```csharp
public interface IVersioned
{
    long Version { get; set; }
}
```

### Store Wrappers

```csharp
var store = new AsyncDataBaseBulkStore<PostgreSQLConnector, Order>();
var versionedStore = new AsyncVersionedStoreWrapper<Order>(store);

// Create assigns Version = 1
await versionedStore.CreateAsync(order);

// Update increments version and checks for conflicts
try
{
    await versionedStore.UpdateAsync(order);  // Checks stored version matches
}
catch (ConcurrentUpdateException ex)
{
    // ex.EntityType, ex.EntityId, ex.ExpectedVersion
    // Reload and retry
}
```

## Stacking Decorators

All patterns use the decorator pattern and can be composed:

```csharp
var baseStore = new AsyncDataBaseBulkStore<PostgreSQLConnector, Order>();

// Stack: versioning -> default -> audit -> timestamp -> soft delete -> base store
var store = new AsyncVersionedStoreWrapper<Order>(
    new AsyncDefaultStoreWrapper<IAsyncBulkStore<Order>, Order>(
        new AsyncAuditStoreWrapper<Order>(
            new AsyncTimestampStoreWrapper<Order>(
                new AsyncSoftDeleteStoreWrapper<Order>(baseStore, clock),
                clock),
            auditContext)));
```

## Store Decorator Composition (Birko.Data.Composition)

`StoreWrapperBuilder.Build<T>()` inspects entity type `T` at runtime and conditionally wraps an `IAsyncBulkStore<T>` with applicable decorators:

```csharp
// Automatically applies Tenant, Default, SoftDelete, Audit, Timestamp
// based on which interfaces T implements
var store = StoreWrapperBuilder.Build<Order>(rawStore, tenantContext, auditContext, clock);
```

**Decorator chain order (outermost to innermost):**
1. **Tenant** — applied if `T : ITenant`
2. **Default** — applied if `T : IDefault`
3. **SoftDelete** — applied if `T : ISoftDeletable`
4. **Audit** — applied if `T : IAuditable`
5. **Timestamp** — applied if `T : ITimestamped`

## See Also

- [Tagging Guide](tagging.md)
- [Birko.Data.Patterns](https://github.com/birko/Birko.Data.Patterns)
