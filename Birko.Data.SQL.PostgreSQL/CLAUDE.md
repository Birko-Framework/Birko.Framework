# Birko.Data.SQL.PostgreSQL

## Overview
PostgreSQL implementation of Birko.Data.SQL stores and repositories.

## Project Location
`C:\Source\Birko.Data.SQL.PostgreSQL\`

## Purpose
- Provides PostgreSQL-specific data store implementations
- PostgreSQL connector management
- Support for PostgreSQL-specific data types

## Components

### Stores
- `PostgreSQLStore<T>` - Synchronous PostgreSQL store
- `PostgreSQLBulkStore<T>` - Bulk operations store
- `AsyncPostgreSQLStore<T>` - Asynchronous PostgreSQL store
- `AsyncPostgreSQLBulkStore<T>` - Async bulk operations store

### Repositories
- `PostgreSQLRepository<T>` - PostgreSQL repository
- `PostgreSQLBulkRepository<T>` - Bulk repository
- `AsyncPostgreSQLRepository<T>` - Async repository
- `AsyncPostgreSQLBulkRepository<T>` - Async bulk repository

### Bulk Insert via COPY Protocol
- `BulkInsert` / `BulkInsertAsync` - Native Npgsql COPY binary protocol for high-throughput bulk inserts
- Uses `BeginBinaryImport` / `BeginBinaryImportAsync` on the connector
- Bypasses SQL parsing for significantly faster bulk data loading
- Supports typed writes with `NpgsqlDbType` for type safety

### Connector
- `PostgreSQLConnector` - PostgreSQL connection management

## Database Connection

Connection string format:
```
Host=server_address;Port=5432;Database=database_name;Username=user;Password=password;
```

## Implementation

```csharp
using Birko.Data.SQL.PostgreSQL.Stores;
using Npgsql;

public class CustomerStore : PostgreSQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO customers (id, name, email)
            VALUES ($1, $2, $3)";

        cmd.Parameters.AddWithValue(item.Id);
        cmd.Parameters.AddWithValue(item.Name);
        cmd.Parameters.AddWithValue(item.Email);

        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

## Bulk Operations

PostgreSQL uses COPY for efficient bulk operations:

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    using (var writer = Connector.BeginBinaryImport("COPY customers (id, name, email) FROM STDIN BINARY"))
    {
        foreach (var item in items)
        {
            writer.StartRow();
            writer.Write(item.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
            writer.Write(item.Name, NpgsqlTypes.NpgsqlDbType.Text);
            writer.Write(item.Email, NpgsqlTypes.NpgsqlDbType.Text);
        }
        writer.Complete();
    }
}
```

## Data Types

Common PostgreSQL to .NET type mappings:
- `UUID` → `Guid`
- `TEXT/VARCHAR(n)` → `string`
- `INTEGER` → `int`
- `BIGINT` → `long`
- `NUMERIC(p,s)` → `decimal`
- `TIMESTAMP` → `DateTime`
- `TIMESTAMPTZ` → `DateTime` — **`ConvertType` maps `DbType.DateTimeOffset` here, but nothing can reach
  it**: `CreateAbstractField` has no `DateTimeOffset` arm and no attribute overrides a field's `DbType`,
  so no model produces a `TIMESTAMPTZ` column today ([[TASK-263]] adds the opt-in)
- `BOOLEAN` → `bool`
- `JSONB` → `string` (or mapped object)
- `ARRAY` → `T[]`

## What a `DateTime` column means here (TASK-256)

> A Birko `DateTime` column on PostgreSQL is `timestamp without time zone` and stores the **wall-clock
> components of the value as supplied**. `DateTimeKind` is **not persisted**; every read returns
> `Kind=Unspecified`. Re-attaching the intended `Kind` is the caller's job.

`PostgreSQLConnector.NormalizeTimestampValue` enforces it at both **un-prepared** write boundaries —
`AddParameter` and the binary `COPY` writer (sync + async) — which is what makes the stored value independent
of the server's `TimeZone` setting.

**The bulk update/delete paths are correct without it, by a different mechanism — don't "fix" them, and
don't copy them.** They bypass `AddParameter`: pre-create parameters holding `DBNull.Value`, call
`command.Prepare()`, then assign `.Value` per row. `Prepare()` pins each parameter to the target column's
real type before any value is assigned, so the value is never re-inferred as `timestamptz`. Measured on a
non-UTC server: unshifted. It is pinned by `Bulk_update_does_not_shift_a_utc_value_on_a_non_utc_server`,
because that `Prepare()` is the only thing holding those six binding sites correct. **A new binding site
that does not prepare must call the helper.** (Provider note: on MSSql `Prepare()` throws on untyped
placeholders, which is why those paths have never worked there at all.)

**Both boundaries, because they failed differently and only one said so.** `AddParameter` binds no `DbType`,
so Npgsql infers `timestamptz` for a `Kind=Utc` value and the server casts it into the timezone-less column
through the session's `TimeZone`; the COPY writer passes `NpgsqlDbType.Timestamp` explicitly and Npgsql
refuses the value outright. Measured on PostgreSQL 16 / Npgsql 10.0.3 with a 10:30 UTC value:

| `Kind` | `AddParameter` before the fix | binary `COPY` before the fix |
|---|---|---|
| `Utc` | stored `11:30` on a UTC+1 server — **silently shifted** | **threw** `ArgumentException` |
| `Local` / `Unspecified` | `10:30`, TZ-independent | `10:30` |

So the fix is narrow: only the `Kind=Utc` cell changes. Fixing COPY alone would have left the two paths
storing different instants, and a bulk-written row would not match a filter bound through the parameterised
path.

**Do not "simplify" this to `TIMESTAMPTZ`.** It was measured and rejected: it makes PostgreSQL the only
tz-aware provider (SQLite, MySQL and MSSql all store wall clocks) so a SQLite-green test stops proving
PostgreSQL behaviour; it breaks the `Unspecified` case (`10:30` in → `09:30Z` back); and
`ALTER COLUMN … TYPE TIMESTAMPTZ` reinterprets existing rows in the session TZ at ALTER time. The column type
is pinned by a test for this reason.

**The premise the helper rests on is not compiler-enforced.** Stripping `Kind` from *every* bound `DateTime`
is safe only while no `DateTime` can target a `timestamptz` column — see the `TIMESTAMPTZ` note under
§ Data Types. [[TASK-263]] falsifies that and must revisit the helper.

**Testing the parameterised half needs a non-UTC server.** On a UTC server both paths store `10:30` either
way, so that revert fails nothing. `Settings.GetConnectionString()` emits no `Timezone` key, so
`SET TimeZone` on a test's own connection cannot reach the store's — `UtcDateTimeBindingLiveTests` creates a
dedicated database with `ALTER DATABASE … SET TimeZone`, and calls `NpgsqlConnection.ClearAllPools()`
afterwards, without which a pooled connection keeps `Etc/UTC` and the test measures nothing.

## PostgreSQL Specific Features

### RETURNING Clause
Get inserted/updated values:
```sql
INSERT INTO customers (name, email)
VALUES ($1, $2)
RETURNING id
```

### Arrays
PostgreSQL supports array columns:
```csharp
cmd.Parameters.AddWithValue(new string[] { "tag1", "tag2" });
```

### JSONB
Native JSON support:
```sql
CREATE TABLE products (
    id UUID PRIMARY KEY,
    data JSONB
);
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores
- Birko.Data.SQL
- Npgsql (PostgreSQL driver)

## Naming Conventions

PostgreSQL commonly uses lowercase with underscores:
- Table names: `customers`, `orders`
- Column names: `customer_id`, `created_at`

## Important Notes

### Settings Handling
Pass `RemoteSettings` through base class:
```csharp
public override void SetSettings(Settings settings)
{
    base.SetSettings(settings); // Correct - creates connector from settings
}
```

Do NOT create settings inline:
```csharp
// WRONG - PasswordSettings doesn't have UserName/Port
var settings = new PasswordSettings { UserName = "...", Port = 5432 };
```

### Parameters
PostgreSQL uses positional parameters ($1, $2, ...) or named parameters:

```csharp
// Positional (recommended for PostgreSQL)
cmd.CommandText = "SELECT * FROM customers WHERE id = $1";

// Named (also works)
cmd.CommandText = "SELECT * FROM customers WHERE id = @id";
```

## Index DDL and identifier case (TASK-245)

PostgreSQL is the one supported provider that **case-folds an unquoted identifier**, and
`AbstractConnector.CreateTable` emits column definitions **bare** — so every column of a PascalCase entity
is stored folded (`status`, `tenantguid`) while the table keeps its case (it is quoted).

`CreateIndexSql` used to wrap each index column in `QuoteIdentifier`, and a quoted `"Status"` cannot resolve
a column stored as `status`: measured on PostgreSQL 16 as `ERROR 42703: column "Status" does not exist`.
**No declared PascalCase index could be created on this provider at all** — silently, because TASK-204 makes
schema-ensure record rather than throw, and every index end-to-end test in the tree ran on case-insensitive
SQLite. Seventh instance of the identifier family (see § Conventions in the aggregator CLAUDE.md).

Fixed by emitting **columns bare, table quoted** in the base emitter. Two consequences to keep in mind:

- **Do not quote an index column identifier here.** It is not a style choice — quoting it is what broke it,
  and the base-table DDL is what settles the convention.
- `PostgreSqlIndexManager.CreateUniqueIndexSql` was **deleted** for the same reason: it carried its own
  quoted-column copy of the statement, so `IIndexManager.CreateAsync` could never build a unique index on a
  PascalCase entity either. Unique index DDL now comes from the connector emitter, which is the single
  producer for every dialect. Reverting the `Unique` flag hand-off in `SqlIndexManager.ToSqlIndexDefinition`
  fails 1 of the PostgreSQL live index suite's 6 tests.

PostgreSQL supports `CREATE INDEX IF NOT EXISTS` natively, so `IsIndexAlreadyExistsException` stays `false`
here — the "already exists" condition never reaches the client. `CreateIndexes(..., throwIfExists: true)`
drops the conditional clause so the flag means the same thing as on MySQL rather than being a silent no-op.

## Limitations
- Requires PostgreSQL 9.5 or later
- Some features may require specific versions

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
