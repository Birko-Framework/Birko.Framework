# Birko.Data.SQL.MySQL

MySQL implementation of Birko.Data.SQL stores and repositories.

## Features

- MySQL stores (sync/async, single/bulk)
- Bulk operations using MySQL bulk loader
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
