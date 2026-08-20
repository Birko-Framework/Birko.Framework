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

## Timestamps — two kinds of `DateTime` column

```csharp
[UtcField]                                  // an INSTANT
public DateTime ObservedAt { get; set; }     // reads back DateTimeKind.Utc

public DateTime NoticeDate { get; set; }     // a WALL CLOCK
                                             // reads back DateTimeKind.Unspecified
```

A plain `DateTime` column stores the value's components exactly as supplied; `DateTimeKind` is not persisted.
A `[UtcField]` one stores an **instant** — normalised to UTC on write, read back as `Kind=Utc`. Neither
preserves a caller's original offset; if you need the offset itself, store it in its own column.

**On SQL Server `[UtcField]` maps to `DATETIMEOFFSET`**, which stores the offset in the column itself, so
the instant is exact there natively. A plain `DateTime` maps to `DATETIME2`.

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
