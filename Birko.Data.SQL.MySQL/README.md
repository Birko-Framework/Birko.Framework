# Birko.Data.SQL.MySQL

MySQL implementation of Birko.Data.SQL stores and repositories.

## Features

- MySQL stores (sync/async, single/bulk)
- Bulk operations using MySQL bulk loader
- Optimized multi-value INSERT batching
- MySQL connector management
- ON DUPLICATE KEY UPDATE (upsert) support

## Installation

```bash
dotnet add package Birko.Data.SQL.MySQL
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.SQL
- MySql.Data

## Usage

```csharp
using Birko.Data.SQL.MySQL.Stores;

public class CustomerStore : MySQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = "INSERT INTO customers (id, name, email) VALUES (@Id, @Name, @Email)";
        cmd.Parameters.AddWithValue("@Id", item.Id);
        cmd.Parameters.AddWithValue("@Name", item.Name);
        cmd.Parameters.AddWithValue("@Email", item.Email);
        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

### Multi-Value INSERT Batching

MySQL bulk stores use optimized multi-value INSERT statements for efficient batch operations:

```csharp
using Birko.Data.SQL.MySQL.Stores;

public class CustomerBulkStore : AsyncMySQLBulkStore<Customer>
{
    public override async Task CreateAsync(IEnumerable<Customer> data,
        StoreDataDelegate<Customer>? storeDelegate = null,
        CancellationToken ct = default)
    {
        // Automatically batches into multi-value INSERT statements:
        // INSERT INTO customers (id, name, email) VALUES
        //   (@Id0, @Name0, @Email0),
        //   (@Id1, @Name1, @Email1),
        //   ...
        await base.CreateAsync(data, storeDelegate, ct);
    }
}
```

## API Reference

### Stores

- **MySQLStore\<T\>** - Sync store
- **MySQLBulkStore\<T\>** - Bulk operations
- **AsyncMySQLStore\<T\>** - Async store
- **AsyncMySQLBulkStore\<T\>** - Async bulk store

### Repositories

- **MySQLRepository\<T\>** / **MySQLBulkRepository\<T\>**
- **AsyncMySQLRepository\<T\>** / **AsyncMySQLBulkRepository\<T\>**

### Connector

- **MySQLConnector** - MySQL connection management

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes

## License

Part of the Birko Framework.
