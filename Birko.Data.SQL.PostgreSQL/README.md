# Birko.Data.SQL.PostgreSQL

PostgreSQL implementation of Birko.Data.SQL stores and repositories.

## Features

- PostgreSQL stores (sync/async, single/bulk)
- Bulk operations using COPY command
- Native support for UUID, JSONB, arrays
- Native bulk insert via Npgsql COPY binary protocol
- PostgreSQL connector management

## Installation

```bash
dotnet add package Birko.Data.SQL.PostgreSQL
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.SQL
- Npgsql

## Usage

```csharp
using Birko.Data.SQL.PostgreSQL.Stores;

public class CustomerStore : PostgreSQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = "INSERT INTO customers (id, name, email) VALUES ($1, $2, $3)";
        cmd.Parameters.AddWithValue(item.Id);
        cmd.Parameters.AddWithValue(item.Name);
        cmd.Parameters.AddWithValue(item.Email);
        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

### Bulk Insert via COPY Protocol

PostgreSQL's COPY binary protocol provides high-throughput bulk inserts, bypassing SQL parsing overhead:

```csharp
using Birko.Data.SQL.PostgreSQL.Stores;

public class CustomerBulkStore : AsyncPostgreSQLBulkStore<Customer>
{
    public override async Task CreateAsync(IEnumerable<Customer> data,
        StoreDataDelegate<Customer>? storeDelegate = null,
        CancellationToken ct = default)
    {
        await using var writer = await Connector.BeginBinaryImportAsync(
            "COPY customers (id, name, email) FROM STDIN (FORMAT BINARY)", ct);

        foreach (var item in data)
        {
            storeDelegate?.Invoke(item);
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(item.Id, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(item.Name, NpgsqlTypes.NpgsqlDbType.Text, ct);
            await writer.WriteAsync(item.Email, NpgsqlTypes.NpgsqlDbType.Text, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
```

## API Reference

### Stores

- **PostgreSQLStore\<T\>** - Sync store
- **PostgreSQLBulkStore\<T\>** - Bulk operations (COPY)
- **AsyncPostgreSQLStore\<T\>** - Async store
- **AsyncPostgreSQLBulkStore\<T\>** - Async bulk store

### Repositories

- **PostgreSQLRepository\<T\>** / **PostgreSQLBulkRepository\<T\>**
- **AsyncPostgreSQLRepository\<T\>** / **AsyncPostgreSQLBulkRepository\<T\>**

### Connector

- **PostgreSQLConnector** - PostgreSQL connection management

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes
- [Birko.Data.TimescaleDB](../Birko.Data.TimescaleDB/) - TimescaleDB (PostgreSQL extension)

## License

Part of the Birko Framework.
