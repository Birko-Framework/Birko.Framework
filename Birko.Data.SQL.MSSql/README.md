# Birko.Data.SQL.MSSql

Microsoft SQL Server implementation of Birko.Data.SQL stores and repositories.

## Features

- SQL Server stores (sync/async, single/bulk)
- Bulk operations using SqlBulkCopy
- MSSql connector management
- Transaction support

## Installation

```bash
dotnet add package Birko.Data.SQL.MSSql
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.SQL
- System.Data.SqlClient (or Microsoft.Data.SqlClient)

## Usage

```csharp
using Birko.Data.SQL.MSSql.Stores;

public class CustomerStore : MSSqlStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"INSERT INTO Customers (Id, Name, Email) VALUES (@Id, @Name, @Email)";
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

- **MSSqlStore\<T\>** - Sync SQL Server store
- **MSSqlBulkStore\<T\>** - Bulk operations (SqlBulkCopy)
- **AsyncMSSqlStore\<T\>** - Async store
- **AsyncMSSqlBulkStore\<T\>** - Async bulk store

### Repositories

- **MSSqlRepository\<T\>** / **MSSqlBulkRepository\<T\>**
- **AsyncMSSqlRepository\<T\>** / **AsyncMSSqlBulkRepository\<T\>**

### Connector

- **MSSqlConnector** - SQL Server connection management

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes
- [Birko.Data.SQL.PostgreSQL](../Birko.Data.SQL.PostgreSQL/) - PostgreSQL provider

## License

Part of the Birko Framework.
