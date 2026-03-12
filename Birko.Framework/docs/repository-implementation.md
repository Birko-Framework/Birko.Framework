# Repository Implementation Guide

## Overview

This guide explains how to implement a new repository in the Birko Framework. Repositories provide business-level data access built on top of stores.

## Repository vs Store

- **Store**: Direct data access to a specific storage backend (CRUD, bulk operations)
- **Repository**: Business-level data access that wraps a store and adds business logic

## Repository Interfaces

### IRepository<T> (Sync)

```csharp
public interface IRepository<T> where T : AbstractModel
{
    T? Read(Guid guid);
    T? Read(Expression<Func<T, bool>>? filter = null);
    Guid Create(T data);
    void Update(T data);
    void Delete(T data);
    long Count(Expression<Func<T, bool>>? filter = null);
    Guid Save(T data);
    T CreateInstance();
    void Init();
    void Destroy();
}
```

### IAsyncRepository<T> (Async)

```csharp
public interface IAsyncRepository<T> where T : AbstractModel
{
    Task<T?> ReadAsync(Guid guid, CancellationToken ct = default);
    Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    Task<Guid> CreateAsync(T data, CancellationToken ct = default);
    Task UpdateAsync(T data, CancellationToken ct = default);
    Task DeleteAsync(T data, CancellationToken ct = default);
    Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    Task<Guid> SaveAsync(T data, CancellationToken ct = default);
    T CreateInstance();
    Task InitAsync(CancellationToken ct = default);
    Task DestroyAsync(CancellationToken ct = default);
}
```

### IBulkRepository<T> (extends IRepository<T>)

```csharp
public interface IBulkRepository<T> : IRepository<T> where T : AbstractModel
{
    IEnumerable<T> Read();
    IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null,
                        int? limit = null, int? offset = null);
    void Create(IEnumerable<T> data);
    void Update(IEnumerable<T> data);
    void Delete(IEnumerable<T> data);
}
```

### IAsyncBulkRepository<T> (extends IAsyncRepository<T>)

```csharp
public interface IAsyncBulkRepository<T> : IAsyncRepository<T> where T : AbstractModel
{
    Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default);
    Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null,
                                    int? limit = null, int? offset = null, CancellationToken ct = default);
    Task CreateAsync(IEnumerable<T> data, CancellationToken ct = default);
    Task UpdateAsync(IEnumerable<T> data, CancellationToken ct = default);
    Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default);
}
```

## Implementation

### Basic Repository (Sync)

```csharp
using Birko.Data.Repositories;
using Birko.Data.Models;

public class ProductRepository : AbstractRepository<Product>
{
    public ProductRepository(IStore<Product> store) : base(store)
    {
    }
}
```

`AbstractRepository<T>` holds a `protected IStore<T>? Store` property and delegates all interface methods to it.

### Async Repository

```csharp
public class ProductAsyncRepository : AbstractAsyncRepository<Product>
{
    public ProductAsyncRepository(IAsyncStore<Product> store) : base(store)
    {
    }
}
```

### Bulk Repository

```csharp
public class ProductBulkRepository : AbstractBulkRepository<Product>
{
    public ProductBulkRepository(IBulkStore<Product> store) : base(store)
    {
    }
}
```

### Async Bulk Repository

```csharp
public class ProductAsyncBulkRepository : AbstractAsyncBulkRepository<Product>
{
    public ProductAsyncBulkRepository(IAsyncBulkStore<Product> store) : base(store)
    {
    }
}
```

## Custom Methods

Add business-specific methods to your repository:

```csharp
public class ProductRepository : AbstractBulkRepository<Product>
{
    public ProductRepository(IBulkStore<Product> store) : base(store)
    {
    }

    public IEnumerable<Product> GetByCategory(string category)
    {
        return Store.Read(p => p.Category == category);
    }

    public Product? GetByName(string name)
    {
        return Store.Read(p => p.Name == name);
    }

    public long CountExpensive(decimal threshold)
    {
        return Store.Count(p => p.Price > threshold);
    }
}
```

## SQL Repository

SQL repositories wrap SQL stores and expose the connector:

```csharp
using Birko.Data.Repositories;
using Birko.Data.SQL.Connectors;

// Sync SQL repository
public class CustomerRepository : DataBaseRepository<Customer, MSSqlConnector>
{
    public CustomerRepository() : base()
    {
        // Creates default DataBaseBulkStore
    }

    public Customer? GetByEmail(string email)
    {
        return Store?.Read(c => c.Email == email);
    }
}

// Async SQL repository
public class CustomerAsyncRepository : AsyncDataBaseRepository<Customer, PostgreSQLConnector>
{
    public CustomerAsyncRepository() : base()
    {
    }

    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return Store != null
            ? await Store.ReadAsync(c => c.Email == email, ct)
            : null;
    }
}
```

SQL repositories also implement `IDataBaseRepository<TConnector, T>` which exposes:
- `TConnector Connector { get; }` - Access to the underlying SQL connector
- `AddOnInit(InitConnector onInit)` / `RemoveOnInit(InitConnector onInit)` - Init callbacks

## Settings Support

Repositories pass settings through to their store:

```csharp
var repo = new CustomerRepository();
repo.SetSettings(new RemoteSettings("localhost", "mydb", "sa", "password", 1433));
repo.Init();
```

## Key API Notes

1. **Model constraint**: `where T : AbstractModel` (not `Entity`)
2. **Read by filter**: `Read(Expression<Func<T, bool>>?)` returns single `T?`
3. **Bulk read**: `Read()` with no args returns all; with filter/orderBy/limit/offset for paging
4. **Delete**: Takes the model object `Delete(T data)`, not a Guid
5. **Count**: Returns `long`
6. **Create**: Returns `Guid` (the assigned ID)
7. **No `ReadAll()`**: Use `Read()` (bulk) or `Read(filter)` (single) instead
8. **Repositories do NOT have StoreDataDelegate**: That's a store-level concern
9. **CancellationToken**: All async methods support cancellation

## Reference Implementations

- **DataBaseRepository** (`Birko.Data.SQL`) - SQL sync repository base
- **AsyncDataBaseRepository** (`Birko.Data.SQL`) - SQL async repository base
- **Birko.Data.ElasticSearch** repositories
- **Birko.Data.MongoDB** repositories
