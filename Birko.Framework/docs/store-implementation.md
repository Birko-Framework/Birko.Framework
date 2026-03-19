# Store Implementation Guide

## Overview

This guide explains how to implement a new store in the Birko Framework. Stores provide direct data access to specific storage backends.

## Store Interfaces

### IStore<T> (Sync)

```csharp
public interface IStore<T> where T : AbstractModel
{
    Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null);
    T? Read(Guid guid);
    T? Read(Expression<Func<T, bool>>? filter = null);
    void Update(T data, StoreDataDelegate<T>? storeDelegate = null);
    void Delete(T data);
    long Count(Expression<Func<T, bool>>? filter = null);
    Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null);
    T CreateInstance();
    void Init();
    void Destroy();
}
```

### IAsyncStore<T> (Async)

```csharp
public interface IAsyncStore<T> where T : AbstractModel
{
    Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);
    Task<T?> ReadAsync(Guid guid, CancellationToken ct = default);
    Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);
    Task DeleteAsync(T data, CancellationToken ct = default);
    Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);
    Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);
    T CreateInstance();
    Task InitAsync(CancellationToken ct = default);
    Task DestroyAsync(CancellationToken ct = default);
}
```

### IBulkStore<T> (extends IStore<T>)

```csharp
public interface IBulkStore<T> : IStore<T> where T : AbstractModel
{
    IEnumerable<T> Read();
    IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null,
                        int? limit = null, int? offset = null);
    void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);
    void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);
    void Delete(IEnumerable<T> data);
}
```

### IAsyncBulkStore<T> (extends IAsyncStore<T>)

```csharp
public interface IAsyncBulkStore<T> : IAsyncStore<T> where T : AbstractModel
{
    Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default);
    Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null,
                                    int? limit = null, int? offset = null, CancellationToken ct = default);
    Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default);
    Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default);
    Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default);
}
```

## Implementation Options

### Option 1: Non-SQL Store (Sync)

```csharp
using Birko.Data.Stores;
using Birko.Data.Models;

public class MyCustomStore<T> : AbstractStore<T> where T : AbstractModel
{
    public override void Init()
    {
        // Initialize storage backend
    }

    public override void Destroy()
    {
        // Clean up / remove data
    }

    public override Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        storeDelegate?.Invoke(data);
        data.Id ??= Guid.NewGuid();
        // Persist data
        return data.Id.Value;
    }

    public override T? Read(Expression<Func<T, bool>>? filter = null)
    {
        // Read with optional filter expression
    }

    public override void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        storeDelegate?.Invoke(data);
        // Update persisted data
    }

    public override void Delete(T data)
    {
        // Soft-delete or mark as deleted
    }

    public override long Count(Expression<Func<T, bool>>? filter = null)
    {
        // Return count matching filter
    }
}
```

### Option 2: Non-SQL Store (Async)

```csharp
public class MyCustomAsyncStore<T> : AbstractAsyncStore<T> where T : AbstractModel
{
    public override async Task InitAsync(CancellationToken ct = default)
    {
        // Initialize storage backend
    }

    public override async Task DestroyAsync(CancellationToken ct = default)
    {
        // Clean up
    }

    public override async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null,
                                                   CancellationToken ct = default)
    {
        processDelegate?.Invoke(data);
        data.Id ??= Guid.NewGuid();
        // Async persist
        return data.Id.Value;
    }

    public override async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null,
                                              CancellationToken ct = default)
    {
        // Async read with filter
    }

    public override async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null,
                                            CancellationToken ct = default)
    {
        processDelegate?.Invoke(data);
        // Async update
    }

    public override async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        // Async delete
    }

    public override async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null,
                                                 CancellationToken ct = default)
    {
        // Async count
    }
}
```

### Option 3: SQL Store

For SQL databases, inherit from `DataBaseStore` or `AsyncDataBaseStore`:

```csharp
using Birko.Data.Stores;
using Birko.Data.SQL.Connectors;

// Sync SQL store
public class MyStore<T> : DataBaseStore<MyConnector, T> where T : AbstractModel
{
    // Inherits Connector property (DB type, protected set)
    // Inherits SetSettings(PasswordSettings) and SetSettings(ISettings)
    // Inherits Init(), Create(), Read(), Update(), Delete(), Count(), Destroy()
}

// Async SQL store
public class MyAsyncStore<T> : AsyncDataBaseStore<MyConnector, T> where T : AbstractModel
{
    // Inherits Connector property (DB? type, protected set)
    // Inherits async equivalents: InitAsync(), CreateAsync(), ReadAsync(), etc.
}
```

SQL store generic constraints: `where DB : AbstractConnector, where T : AbstractModel`

## Adding Bulk Operations

### Sync Bulk Store

```csharp
public class MyBulkStore<T> : AbstractBulkStore<T> where T : AbstractModel
{
    // Inherits all IStore<T> methods from parent

    // Additional bulk methods:
    public override IEnumerable<T> Read()
    {
        // Read all items
    }

    public override IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null,
                                         OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        // Read with filter, ordering, and paging
    }

    public override void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // Bulk create
    }

    public override void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // Bulk update
    }

    public override void Delete(IEnumerable<T> data)
    {
        // Bulk delete
    }
}
```

### SQL Bulk Stores

```csharp
// Sync
public class MySqlBulkStore<T> : DataBaseBulkStore<MyConnector, T> where T : AbstractModel
{
    // Inherits all sync CRUD + bulk operations
}

// Async
public class MyAsyncSqlBulkStore<T> : AsyncDataBaseBulkStore<MyConnector, T> where T : AbstractModel
{
    // Inherits all async CRUD + bulk operations
}
```

## Connector Pattern

SQL stores use connectors for database communication. Connectors are **siblings**, not parent-child:

```
AbstractConnectorBase (shared: settings, type conversion, field definitions)
    -> AbstractConnector (sync: DoCommand, DoInit)
    -> AbstractAsyncConnector (async: DoCommandAsync, DoInitAsync)
```

```csharp
public abstract class AbstractConnectorBase
{
    protected readonly PasswordSettings _settings;
    public bool IsInitializing { get; protected set; }

    protected AbstractConnectorBase(PasswordSettings settings);
    abstract DbConnection CreateConnection(PasswordSettings settings);
    abstract string ConvertType(DbType type, Fields.AbstractField field);
    abstract string FieldDefinition(Fields.AbstractField field);
    virtual string QuoteIdentifier(string identifier);
    virtual DbCommand AddParameter(DbCommand command, string name, object value);
}
```

Provider connectors (all in `Birko.Data.SQL.Connectors` namespace):
- `PostgreSQLConnector` - PostgreSQL
- `MSSqlConnector` - Microsoft SQL Server
- `MySQLConnector` - MySQL
- `SqLiteConnector` - SQLite

## Settings (Birko.Settings)

Settings classes live in the `Birko.Settings` shared project (namespace `Birko.Configuration`). They are transitively imported by `Birko.Data.Stores`.

### Settings Chain

```
ISettings (GetId)
    -> Settings (Location, Name)
        -> PasswordSettings (+Password)
            -> RemoteSettings (+UserName, +Port)
```

```csharp
// File-based stores (JSON, SQLite)
var fileSettings = new Settings("./data", "mydata");

// Password-protected stores
var pwSettings = new PasswordSettings("./data", "local.db", "optional-password");

// Remote database stores (PostgreSQL, MySQL, MSSql, MongoDB, etc.)
var remoteSettings = new RemoteSettings("server.local", "mydb", "username", "password", 5432);
```

### Setting Settings on Stores

SQL stores implement `ISettingsStore<ISettings>` and `ISettingsStore<PasswordSettings>`:

```csharp
var store = new AsyncDataBaseStore<PostgreSQLConnector, MyModel>();
store.SetSettings(new RemoteSettings("localhost", "mydb", "admin", "secret", 5432));

// SQLite uses PasswordSettings
var sqliteStore = new DataBaseStore<SqLiteConnector, MyModel>();
sqliteStore.SetSettings(new PasswordSettings("./data", "local.db", "password"));
```

**Important:** Pass settings via `SetSettings()`, don't construct connectors manually.

## Transaction Support

SQL stores implement transactional interfaces:

```csharp
// Sync: ITransactionalStore<T, SqlTransactionContext>
store.SetTransactionContext(transactionContext);

// Async: IAsyncTransactionalStore<T, SqlTransactionContext>
asyncStore.SetTransactionContext(transactionContext);
```

## Initialization Events

SQL stores support init callbacks:

```csharp
store.AddOnInit((connector) => {
    // Called when connector initializes (create tables, etc.)
});
```

## Key API Notes

1. **Model constraint**: All stores use `where T : AbstractModel` (not `Entity`)
2. **Read by ID**: `Read(Guid guid)` / `ReadAsync(Guid guid)` returns `T?`
3. **Read by filter**: `Read(Expression<Func<T, bool>>?)` returns single `T?`
4. **Bulk read**: `Read()` returns `IEnumerable<T>`, supports filter/orderBy/limit/offset
5. **Delete**: Takes the model object `Delete(T data)`, not a Guid
6. **Destroy**: Takes no arguments - `Destroy()` / `DestroyAsync()`
7. **Count**: Returns `long` (not `int`)
8. **Create**: Returns `Guid` (the assigned ID)
9. **StoreDataDelegate**: Optional callback for pre-processing data before persistence
10. **CancellationToken**: All async methods accept `CancellationToken ct = default`
11. **Connector property**: `protected set` - derived classes can modify

## Reference Implementations

- **JSON Store** (`Birko.Data.JSON`) - Simple file-based implementation
- **ElasticSearch Store** (`Birko.Data.ElasticSearch`) - Full async/bulk reference
- **MongoDB Store** (`Birko.Data.MongoDB`) - NoSQL document store reference
- **MSSql/PostgreSQL/MySQL stores** - SQL-specific patterns
