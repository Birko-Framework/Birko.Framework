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

## Timestamps — and one thing to set when you stand the server up

Two kinds of `DateTime` column, chosen per property:

```csharp
public class Reading : AbstractDatabaseLogModel
{
    [UtcField]                                  // an INSTANT -> TIMESTAMPTZ
    public DateTime ObservedAt { get; set; }     // reads back DateTimeKind.Utc

    public DateTime NoticeDate { get; set; }     // a WALL CLOCK -> TIMESTAMP
}                                                // reads back DateTimeKind.Unspecified
```

- A **plain `DateTime`** column stores the value's date and time components exactly as supplied.
  `DateTimeKind` is not persisted, and every read returns `Unspecified`. Right for a local calendar date;
  it cannot tell you *which instant* it names.
- A **`[UtcField]` `DateTime`** column stores an instant: normalised to UTC on write, kept in `TIMESTAMPTZ`,
  and read back as `Kind=Utc`. It does **not** preserve a caller's original offset — that is normalised away
  on every provider, deliberately, so the behaviour is identical on SQLite/MySQL/MSSql where no
  timezone-aware column type exists. If you need the offset itself, store it in its own column.

### Recommended: run the server with `timezone = 'UTC'`

Not required — both column kinds store and return the same values whatever the server's time zone, and the
framework never emits a server-side clock (`now()`, `CURRENT_TIMESTAMP`) so nothing local leaks in. It is
recommended because of what you see when you look at the data **outside** the framework:

| Server time zone | plain `DateTime` shows | `[UtcField]` shows |
|---|---|---|
| `UTC` | `10:30:00` | `10:30:00+00` |
| `Europe/Bratislava` | `10:30:00` | `11:30:00+01` |

Both rows hold the same instants. But on a non-UTC server `psql`, a BI tool, a report or a hand-written
migration sees a `[UtcField]` column rendered in local time (correct, but it looks wrong), while a plain
column shows a bare `10:30:00` with no offset marker that looks like local time and is not — it is the UTC
wall clock. On a UTC server the two are unambiguous and nothing downstream has to know which kind a column is.

> ⚠ **If you are upgrading an existing PostgreSQL deployment**, make sure the framework build you deploy
> includes the `DateTime` binding fix (TASK-256) *before* any rows are written. Older builds bound a
> `Kind=Utc` value in a way PostgreSQL re-read through the session time zone, so on a non-UTC server they
> stored a shifted instant with no error — and rows written before and after the fix are indistinguishable
> in the column, so no bulk `UPDATE` can repair them afterwards. On a UTC server the old and new behaviour
> agree, which is the other reason to prefer it.

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
