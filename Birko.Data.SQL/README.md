# Birko.Data.SQL

SQL-specific data access layer providing abstract base classes for SQL database operations in the Birko Framework.

## Features

- Abstract SQL store classes (sync and async, with bulk support)
- Typed connector pattern for database connections
- SQL attributes for table/column mapping
- Repository base classes for SQL databases
- Query caching layer with automatic invalidation
- Automatic connection management via Settings

## Installation

```bash
dotnet add package Birko.Data.SQL
```

## Dependencies

- Birko.Data.Core (AbstractModel, models)
- Birko.Data.Stores (store interfaces, Settings, RemoteSettings)
- .NET 10.0

## Usage

```csharp
using Birko.Data.SQL.Stores;

public class CustomerStore : DataBaseStore<SqlConnection, Customer>, IStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = "INSERT INTO Customers (Id, Name) VALUES (@Id, @Name)";
        cmd.Parameters.AddWithValue("@Id", item.Id);
        cmd.Parameters.AddWithValue("@Name", item.Name);
        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

## API Reference

### Stores

- **DataBaseStore\<DB,T\>** - Base sync SQL store with `Connector` property
- **DataBaseBulkStore\<DB,T\>** - Extends with bulk operations
- **AsyncDataBaseStore\<DB,T\>** - Base async SQL store
- **AsyncDataBaseBulkStore\<DB,T\>** - Async with bulk operations

### Repositories

- **DataBaseRepository\<T,S,DB\>** - SQL repository base
- **DataBaseBulkRepository\<T,S,DB\>** - Bulk repository
- **AsyncDataBaseRepository\<T,S,DB\>** - Async repository
- **AsyncDataBaseBulkRepository\<T,S,DB\>** - Async bulk repository

### Attributes (`Birko.Data.SQL.Attributes`)

- **Table(string name)** - Maps entity class to a database table
- **NamedField(string? name)** - Maps property to a column with a custom name. Column names are emitted unquoted, so a
  property whose name the provider reserves (`Order`, `Group`, `Select`; `Rank` and `Key` on MySQL; `User` on
  PostgreSQL and SQL Server) is refused with `NotSupportedException` when its table is built. Map it to another
  column name with this attribute or a fluent `HasColumnName` (TASK-862).
- **PrimaryField** - Marks primary key column
- **UniqueField** - Marks column as unique
- **IncrementField** - Marks column as auto-increment
- **RequiredField** - Forces NOT NULL even for nullable C# types
- **MaxLengthField(int maxLength)** - Sets VARCHAR length for string fields
- **PrecisionField(int precision)** - Sets numeric precision
- **ScaleField(int scale)** - Sets numeric scale
- **IgnoreField** - Excludes a property from SQL field mapping

### DataAnnotations Support

Standard `System.ComponentModel.DataAnnotations` attributes are recognized alongside Birko attributes (Birko takes precedence when both are specified):

- `[Table]` / `[Column]` - Table and column name mapping
- `[Key]` - Primary key (same as `[PrimaryField]`)
- `[Required]` - NOT NULL (same as `[RequiredField]`)
- `[MaxLength]` / `[StringLength]` - VARCHAR length (same as `[MaxLengthField]`)
- `[DatabaseGenerated(Identity)]` - Auto-increment (same as `[IncrementField]`)
- `[NotMapped]` - Exclude from mapping (same as `[IgnoreField]`)

### Settings

Uses `SqlSettings` (extends `RemoteSettings`) with `CommandTimeout`, `ConnectionTimeout`, and abstract `GetConnectionString()` overridden by each provider:

- **MSSqlSettings** — `MultipleActiveResultSets`, `TrustServerCertificate`
- **MySqlSettings** — `BulkInsertBatchSize`
- **PostgreSqlSettings** — `UseBinaryImport`
- **SqLiteSettings** (extends `PasswordSettings`) — `CommandTimeout`

```csharp
var settings = new MSSqlSettings
{
    Location = "localhost",
    Name = "MyDatabase",
    UserName = "sa",
    Password = "password",
    TrustServerCertificate = true,
    CommandTimeout = 60
};
var store = new MSSqlStore<Customer>();
store.SetSettings(settings);
```

### Schemas (TASK-272)

An entity declares its schema on its table; a connection can set a default for every table that declares none.

```csharp
[Table("Events", Schema = "reporting")]           // or DataAnnotations [Table("Events", Schema = "reporting")]
public class Event : AbstractModel { /* ... */ }

map.ToTable("Events", "reporting");                // fluent mapping (Birko.Models.SQL)

var settings = new PostgreSqlSettings(/* ... */) { Schema = "app" };   // the connection's default schema
```

| Provider | `reporting.Events` is stored as |
|---|---|
| PostgreSQL, TimescaleDB, SQL Server | the table `Events` in schema `reporting` (created with the table if missing) |
| MySQL, SQLite | the table `reporting__Events` in the same database (a MySQL schema is a separate database) |

The entity's schema wins over the settings' default. A schema must be a plain identifier. A model that two providers would
resolve to one table is refused when it loads: `reporting.Events` beside a table declared as `reporting__Events`, or one
logical name in two schemas. The table's name stays its alias, so filters and joins are unchanged.

Every statement naming the table honours the schema: DML, bulk copy, `CREATE`/`ALTER`/`DROP TABLE`, index DDL, the index
managers, drift detection and repair, missing-table healing, view DDL (SQL Server SCHEMABINDING included), hypertables, and
the migration schema builder and data migrator. Deliberately not: a view's own name (views are created in the connection's
default schema), the migrations history table (`SqlMigrationSettings.Schema`), and `ViewSqlGenerator`, which has no
connector and refuses a view over a table with a schema.

### Index Management

```csharp
using Birko.Data.SQL.IndexManagement;
using Birko.Data.SQL.PostgreSQL.IndexManagement;
using Birko.Data.Patterns.IndexManagement;

// Dialect-specific managers
var indexManager = new PostgreSqlIndexManager(connector);  // or MSSqlIndexManager, SqLiteIndexManager, MySqlIndexManager

// Same IIndexManager interface as NoSQL providers
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "idx_order_date",
    Fields = new[] { IndexField.Ascending("CustomerId"), IndexField.Descending("CreatedAt") },
    Unique = false
}, scope: "Orders");

// List, check, drop
var indexes = await indexManager.ListAsync(scope: "Orders");
bool exists = await indexManager.ExistsAsync("idx_order_date", scope: "Orders");
await indexManager.DropAsync("idx_order_date", scope: "Orders");
```

**Dialect-specific SQL generation:**
| Provider | Catalog View | Quoting |
|----------|-------------|---------|
| PostgreSQL | `pg_indexes` + `pg_class` | `"identifier"` |
| MSSQL | `sys.indexes` + `sys.index_columns` | `[identifier]` |
| MySQL | `information_schema.statistics` | `` `identifier` `` |
| SQLite | `sqlite_master` + `PRAGMA index_info` | `"identifier"` |

### Adding Missing Columns

Schema-ensure creates a table that does not exist but never alters one that does, so a table created before a
property had a column mapping keeps lacking that column. Close the gap explicitly — at startup or in a migration:

```csharp
var added = store.Connector.EnsureColumns(typeof(UsageRecord)); // the ColumnDrift entries it closed
```

- Adds only the columns `DetectDrift` reports as `Missing`; never drops or retypes. Running it again adds nothing.
- Existing rows read back as `default(T)` for each new property (a NOT NULL column is added with that default).
- Refuses, before changing anything, a missing column that is a primary key, unique, identity, or a
  `[RequiredField]` string / `byte[]` — those need a migration that supplies the value.
- Opt-in: it is never run by a store on its own.

To review the repair instead of running it (rehearse it on a copy, ship it with a deploy), ask for the statements:

```csharp
var plan = store.Connector.PlanRepair(typeof(UsageRecord));
foreach (var sql in plan.Statements) Console.WriteLine(sql);       // the ADDs EnsureColumns would run, and renames
foreach (var skip in plan.NotActioned) Console.WriteLine(skip);     // type, nullability, key, index, extra columns: yours
foreach (var need in plan.Requirements) Console.WriteLine(need);    // e.g. SQLite decimal: register the collation first
```

- A missing column becomes the same `ALTER TABLE … ADD` `EnsureColumns` runs; its `DEFAULT` is what fills the existing rows.
- A column stored under another case (PostgreSQL, a case-sensitive SQL Server) is renamed, keeping its values.
- Nothing that can lose data is written. The framework never runs the plan (TASK-633).

### Query Caching

- **CachedAsyncDataBaseBulkStore\<DB,T\>** - Caching decorator for async bulk stores with automatic invalidation

```csharp
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.Caching;

var cacheOptions = new SqlCacheOptions
{
    DefaultExpiration = TimeSpan.FromMinutes(10),
    KeyPrefix = "customers"
};

var innerStore = new AsyncPostgreSQLBulkStore<Customer>();
var cachedStore = new CachedAsyncDataBaseBulkStore<NpgsqlConnection, Customer>(
    innerStore, cache, cacheOptions);

// Reads are served from cache when available
var customer = await cachedStore.ReadAsync(customerId);

// Writes automatically invalidate affected cache entries
await cachedStore.UpdateAsync(customer);
```

Key components:
- **SqlCacheKeyBuilder** - Generates consistent cache keys from query parameters and filters
- **SqlCacheOptions** - Configuration for cache TTL, key prefix, and invalidation strategy

## Database Providers

- [Birko.Data.SQL.MSSql](../Birko.Data.SQL.MSSql/) - SQL Server
- [Birko.Data.SQL.PostgreSQL](../Birko.Data.SQL.PostgreSQL/) - PostgreSQL
- [Birko.Data.SQL.MySQL](../Birko.Data.SQL.MySQL/) - MySQL
- [Birko.Data.SQL.SqLite](../Birko.Data.SQL.SqLite/) - SQLite
- [Birko.Data.TimescaleDB](../Birko.Data.TimescaleDB/) - TimescaleDB

## Filter-Based Bulk Operations

SQL stores support native filter-based update and delete — single SQL commands instead of per-row operations:

```csharp
// Native UPDATE ... SET active=0 WHERE category='old' (single SQL command)
await store.UpdateAsync(
    x => x.Category == "old",
    new PropertyUpdate<Product>().Set(x => x.Active, false).Set(x => x.Category, "archived"));

// Native DELETE FROM ... WHERE is_expired=1 (single SQL command)
await store.DeleteAsync(x => x.IsExpired);

// Complex mutations use read-modify-save fallback
await store.UpdateAsync(x => x.Price > 100, item => { item.Price *= 0.9m; });
```

`PropertyUpdate<T>` assignments are translated to SQL SET clauses by `Stores/PropertyUpdateSqlTranslator.cs` (shared by the sync and async bulk stores): a Set renders `col = @SETcol`, an `Increment` / `Decrement` renders `col = col + @SETcol`, all in one UPDATE. Column names come from table metadata and are emitted bare (rule 17 — framework DDL creates them unquoted); the connector quotes the table. The filter expression is converted to a WHERE clause via `DataBase.ParseConditionExpression()`.

### Filter translation notes

- **Empty membership sets are safe.** `ids.Contains(x.Id)` with an empty collection renders `1 = 0` (matches
  nothing) and its negation renders `1 = 1` (matches everything) — never `IN ()`, which PostgreSQL and MSSQL
  reject as a syntax error.
- **Enum collections work.** `statuses.Contains(x.Status)` translates to a real `IN` on all providers; enum
  parameters bind as their underlying integer, matching how enums are persisted (`INTEGER`).
- **String matching (TASK-869).** Without a comparison, `Contains` / `StartsWith` / `EndsWith` render `LIKE` and
  follow the column's collation, so they differ per provider and from C#:

  | Provider (default collation) | Case | Accents | e.g. `Contains("caf")` matches |
  |---|---|---|---|
  | SQLite | insensitive for ASCII only | sensitive | `café`, `CAFÉ` (but `Contains("é")` misses `É`) |
  | PostgreSQL (deterministic) | sensitive | sensitive | `café` only (as C#) |
  | MySQL 8 (`utf8mb4_0900_ai_ci`) | insensitive | **insensitive** | `café`, `CAFE`, `Café`… and `Contains("cafe")` matches `café` |
  | SQL Server (`SQL_Latin1_General_CP1_CI_AS`) | insensitive | sensitive | `café`, `CAFÉ`, `Café` |

  `x.Title.ToLower().Contains(q.ToLower())` follows the collation too (SQL's `LOWER` is not .NET's: SQLite folds
  ASCII only). For C#'s answer on every provider, pass the comparison:
  - `StringComparison.Ordinal` — exact, case- and accent-sensitive.
  - `StringComparison.OrdinalIgnoreCase` — C#'s own case classes (`ı` and `ſ` stay apart from `I` / `S`, as in C#),
    accent-sensitive. On SQL Server a term holding a cased letter outside the BMP (e.g. Deseret) is refused, and so is
    a term whose LIKE pattern passes SQL Server's 4000-character limit (each cased letter costs four: `[Aa]`).
  - SQL Server's 4000-character LIKE limit applies to the no-argument forms too (TASK-874): the pattern counts its
    `%` wildcards and an escape per `%` / `_` / `[` / `!`, so a `Contains` term past 3998 plain characters throws
    `NotSupportedException` before anything runs. The other providers have no such limit.
  - On MySQL `Ordinal` names `COLLATE utf8mb4_bin`, so the column must be `utf8mb4` (every framework-created table is);
    on a `utf8mb3` / `latin1` column MySQL fails the statement with ERROR 1253.
  - `string.Equals(x.Col, v, comparison)` and `x.Col.Equals(v, comparison)` translate the same way.
  - Any other comparison (`CurrentCulture*`, `InvariantCulture*`) and the `(string, bool, CultureInfo)` overloads
    throw `NotSupportedException` before anything runs. Before TASK-869 the comparison was silently ignored.
- **Two columns compare** (`x.A == x.B`, `x.Opened < x.Closed`); `==` / `!=` on nullable columns treat two nulls
  as equal, as C# does. `x.Opened.Date == x.Closed.Date` throws `NotSupportedException` (no portable SQL form).
- Predicates also accept ternaries, `??`, and column arithmetic — see
  [CLAUDE.md § Filter translation](CLAUDE.md) for the full supported surface.

## License

Part of the Birko Framework.
