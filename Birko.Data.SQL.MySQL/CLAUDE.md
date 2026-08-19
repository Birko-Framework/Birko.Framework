# Birko.Data.SQL.MySQL

## Overview
MySQL implementation of Birko.Data.SQL stores and repositories.

## Project Location
`C:\Source\Birko.Data.SQL.MySQL\`

## Purpose
- Provides MySQL-specific data store implementations
- MySQL connector management
- Support for MySQL-specific data types

## Components

### Stores
- `MySQLStore<T>` - Synchronous MySQL store
- `MySQLBulkStore<T>` - Bulk operations store
- `AsyncMySQLStore<T>` - Asynchronous MySQL store
- `AsyncMySQLBulkStore<T>` - Async bulk operations store

### Repositories
- `MySQLRepository<T>` - MySQL repository
- `MySQLBulkRepository<T>` - Bulk repository
- `AsyncMySQLRepository<T>` - Async repository
- `AsyncMySQLBulkRepository<T>` - Async bulk repository

### Bulk Operations
- Multi-value INSERT batching for optimized bulk inserts
- Batches multiple rows into single `INSERT INTO ... VALUES (...), (...), ...` statements
- Configurable batch size for memory/performance tuning
- Significantly faster than individual INSERT statements

### Connector
- `MySQLConnector` - MySQL connection management

## Database Connection

Connection string format:
```
Server=server_address;Port=3306;Database=database_name;User Id=user;Password=password;
```

## Implementation

```csharp
using Birko.Data.SQL.MySQL.Stores;
using MySql.Data.MySqlClient;

public class CustomerStore : MySQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO customers (id, name, email)
            VALUES (@Id, @Name, @Email)";

        cmd.Parameters.AddWithValue("@Id", item.Id);
        cmd.Parameters.AddWithValue("@Name", item.Name);
        cmd.Parameters.AddWithValue("@Email", item.Email);

        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

## Bulk Operations

MySQL uses LOAD DATA INFILE for bulk operations:

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    // Bulk insert implementation using MySQL bulk loader
}
```

## Data Types

Common MySQL to .NET type mappings:
- `CHAR(36)` / `BINARY(16)` → `Guid`
- `VARCHAR(n)` / `TEXT` → `string`
- `INT` → `int`
- `BIGINT` → `long`
- `DECIMAL(p,s)` → `decimal`
- `DATETIME` → `DateTime`
- `TIMESTAMP` → `DateTime`
- `TINYINT(1)` → `bool`
- `JSON` → `string` (or mapped object)


**`[UtcField]` and the two meanings of a `DateTime` column (TASK-256 / TASK-263).** A plain Birko `DateTime`
column is a **wall clock** — the value's components as supplied, `Kind` not persisted, reads back
`Unspecified`. Marking the property `[UtcField]` makes it an **instant**: `DbType.DateTimeOffset`, and it reads
back `Kind=Utc` on every provider. Both coexist per property on one entity.
MySQL has no timezone-aware type this framework maps to, so it **falls back** to plain `DATETIME`: the
offset is dropped and the UTC wall clock stored. The instant is still exact, recoverable because both
sides agree the column holds UTC — which is what the attribute declares. Measured: a non-UTC session does
not shift it, unlike PostgreSQL's `timestamptz`.

## MySQL Specific Features

### AUTO_INCREMENT
For tables with auto-increment:
```sql
INSERT INTO customers (name, email)
VALUES (@Name, @Email);
SELECT LAST_INSERT_ID();
```

### ON DUPLICATE KEY UPDATE
Upsert pattern:
```sql
INSERT INTO customers (id, name, email)
VALUES (@Id, @Name, @Email)
ON DUPLICATE KEY UPDATE name = VALUES(name), email = VALUES(email);
```

### UUID Functions
MySQL UUID functions:
```sql
INSERT INTO customers (id, name)
VALUES (UUID(), @Name);
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores
- Birko.Data.SQL
- MySql.Data (MySQL connector)

## Naming Conventions

MySQL commonly uses lowercase with underscores:
- Table names: `customers`, `orders`
- Column names: `customer_id`, `created_at`

## Important Notes

### Settings Handling
Pass `RemoteSettings` through base class:
```csharp
public override void SetSettings(Settings settings)
{
    base.SetSettings(settings); // Correct
}
```

Do NOT create settings inline:
```csharp
// WRONG - PasswordSettings doesn't have UserName/Port
var settings = new PasswordSettings { UserName = "...", Port = 3306 };
```

### Parameters
MySQL uses named parameters with @ prefix:
```csharp
cmd.Parameters.AddWithValue("@Id", item.Id);
cmd.Parameters.AddWithValue("@Name", item.Name);
```

### Guid Storage
MySQL doesn't have a native UUID type. Options:
- `CHAR(36)` - String representation (more readable)
- `BINARY(16)` - Compact storage (requires conversion)

## Index DDL (TASK-245)

MySQL has **no conditional form for `CREATE INDEX`** — `CREATE INDEX IF NOT EXISTS` is `ERROR 1064`, a
syntax error. Before TASK-245 the framework emitted exactly that, so **every** `[IndexedField]` /
`[CompositeIndex]` on a MySQL entity produced no index and, for a UNIQUE one, no constraint. It was silent
because TASK-204 makes schema-ensure record rather than throw and nothing subscribes to the report.

`MySQLConnector` therefore overrides three things:

| member | why |
|---|---|
| `CreateIndexSql` | never emits `IF NOT EXISTS`; columns bare, table quoted (inherited from the base) |
| `DropIndexSql` | ``DROP INDEX `n` ON `T` `` — MySQL takes no `IF EXISTS` and **requires** the `ON` clause; the base got both wrong |
| `IsIndexAlreadyExistsException` | matches error **1061** (`Duplicate key name`) so `CreateIndexes` can fake the conditional form |

Error codes that matter here, all measured on MySQL 8.4:

- **1061** `Duplicate key name` — "already there". Tolerated at the `CreateIndexes` funnel, which is what
  the other three providers already report as success. Also fires for a same-name index over *different*
  columns, so such an index is silently accepted — faithful, since PostgreSQL's `IF NOT EXISTS` and MSSql's
  `sys.indexes` guard are both name-only too.
- **1062** `Duplicate entry` — a UNIQUE index over data that already violates it. Genuinely unbuildable, so
  it is **not** tolerated: recorded by schema-ensure, thrown by the explicit path. TASK-204 intact.
- **1091** `Can't DROP` — dropping an absent index now throws here, where the base's `IF EXISTS` tolerated
  it. Deliberate: a `DropIndexes` caller named a specific index.
- **1170** `BLOB/TEXT column used in key specification without a key length` — **fixed by bounding the
  column** (TASK-248). An unbounded `string` maps to `LONGTEXT` (see Data Types) and MySQL cannot index a
  BLOB/TEXT column at all, so `ConvertType` emits `VARCHAR(255)` — see `IndexedStringColumnLength` — whenever
  `AbstractField.IsIndexed` is set. Unindexed strings stay `LONGTEXT`; an explicit `[MaxLengthField(n)]` still
  wins. This bound exists **only on MySQL**: the other three providers index TEXT natively and seven live
  consumer entities rely on that, so the divergence is this provider's index-key limit, not a framework
  choice. A prefix index (`ux(Col(64))`) was rejected because every real case is UNIQUE and a prefix makes the
  constraint weaker than declared.

Match on the **code**, never the message, and walk `InnerException` — `AbstractConnector.InitException`
re-wraps every command failure as `new Exception(commandText, ex)`.

## Limitations
- Requires MySQL 5.7 or later
- JSON type requires MySQL 5.7.8+
- Some features may vary by MySQL edition
- **An indexed `string` is silently capped at 255 characters** unless `[MaxLengthField(n)]` says otherwise
  (TASK-248) — MySQL cannot index an unbounded `LONGTEXT` at all, so the column is emitted as `VARCHAR(255)`.
  Declare the length explicitly when a longer indexed value is genuinely needed, and remember InnoDB's
  3072-byte index-key limit is the real ceiling. Unindexed strings are unaffected.
- **`byte[]` (`LONGBLOB`) still cannot be indexed** — same 1170 restriction, no equivalent bound applied,
  because nothing in the tree declares an index over a `byte[]`. It is recorded, not thrown.

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
