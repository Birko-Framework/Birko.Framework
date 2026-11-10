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

### Aggregation

`Aggregate` / `AggregateAsync` (`AggregateQuery<T>`) run one `GROUP BY` statement and answer what the in-memory
`AggregateHelper` answers on every other store (TASK-892):

- **Types.** Each value is converted to `AggregateMath.ResultType`: Count an `int`; Sum as the property is (a smaller
  integer as `long`); Avg of an integer a `double`; Min / Max the property's type, a non-numeric one read by the column's
  own reader (SQLite's text dates included). A group key is read the same way and named as the query names it
  (PostgreSQL used to answer `kind` for `Kind`). A Sum or Avg of a non-numeric property is refused, as LINQ refuses it.
- **Average.** An integral column is averaged as a double (SQL Server averaged it as an integer: 1 for 1, 1, 2). A
  decimal average is exact: SQLite's `birko_decimal_avg`; on a server, which keeps its own scale, the SUM and COUNT are
  read and divided in C#'s decimal. Ordering by that alias uses the server's `AVG`, so averages that differ only past the
  server's scale (SQL Server: 6 decimals) may order as ties.
- **Sum over no values** (a group of only NULLs) is NULL, as `AggregateHelper` answers. A view's SUM answers 0 there,
  as LINQ's `Sum` fills a property.
- **Recorded, not fixed:** a SUM / AVG of `double` values adds the rows in the server's order, so it can differ from
  LINQ in the last digit; an integral average whose sum passes 2^53 is a double sum (LINQ sums in `long`); an ungrouped
  aggregate over no rows answers one row on SQL and none in LINQ (TASK-886); a time bucket needs TimescaleDB.

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

  `x.Title.ToLower().Contains(q.ToLower())` follows the collation too. The fold itself (TASK-870): on SQLite
  `ToLower()` / `ToUpper()` and their invariant forms are C#'s own (a registered function, `birko_case_fold`; SQLite's
  `LOWER` folds ASCII only, so `Čučoriedka` stayed uppercase). On the servers they are `LOWER` / `UPPER`, which fold by
  the server's case tables: measured over 34 texts, PostgreSQL differs from .NET on 1 (the Turkish `İ` / `ı` under the
  invariant forms), MySQL on up to 2, SQL Server on 7 to 8 (`K` Kelvin, `ſ`, `İ`, `ı`, …). `č`, `ä` and `é` fold as
  in C# on all four (TASK-870's view tests). For C#'s answer on every provider, pass the comparison:
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
- **String equality is exact (TASK-873) — BREAKING on MySQL and SQL Server.** `==` / `!=` on a string, a list's
  `Contains` (`IN` / `NOT IN`), `Equals` without a comparison, two string columns compared, a case fold or a `??` compared
  with a value, and `string.IsNullOrEmpty` give C#'s ordinal answer on every provider, in reads, counts, updates, deletes
  and view filters. Before, `=` followed the column's collation (measured over 134 shapes: 97 off on MySQL, 113 on SQL
  Server, none on SQLite or PostgreSQL):

  | Provider (default collation) | `x.Code == "abc"` used to match | also |
  |---|---|---|
  | SQLite (`BINARY`) | `abc` only (as C#) | a column declared `NOCASE` / `RTRIM` elsewhere matched `ABC` / `abc ` |
  | PostgreSQL (deterministic) | `abc` only (as C#) | a column with a nondeterministic ICU collation matched `ABC` (`citext` compared with Npgsql's `text` parameter was already exact) |
  | MySQL 8 (`utf8mb4_0900_ai_ci`) | `abc`, `ABC`, `Abc` | `café` = `cafe`, `ß` = `ss`, `K` (Kelvin) = `K`, `a b` (no-break space) = `a b` |
  | SQL Server (`SQL_Latin1_General_CP1_CI_AS`) | `abc`, `ABC`, `Abc`, `abc ` | `ß` = `ss`  The rendering keeps the plain comparison in front, so an index on the column still answers it, and adds the exact
  one: `(Code = @p AND exact(Code) = exact(@p))`, and `IN` the same way; a negation (`!=`, `NOT IN`) is `NOT (…)` of
  that. `exact` is `CONVERT(x USING utf8mb4) COLLATE utf8mb4_0900_bin` on MySQL (NO PAD; any column character set,
  MySQL 8.0+), `(x + N'|') COLLATE Latin1_General_100_BIN2` on SQL Server on both sides (the terminator defeats the
  padding; a value of 4000 characters or more is also offered as `CAST(@p AS NVARCHAR(MAX)) + N'|'`, since SQL Server
  cuts a non-`MAX` concatenation at 4000; two columns compared are both cast to `NVARCHAR(MAX)`), `(x)::text COLLATE "C"`
  on PostgreSQL and `(x) COLLATE BINARY` on SQLite. Measured on 100,000 rows: `Code COLLATE … = @p` alone scanned the
  whole index on SQL Server and MySQL and was a sequential scan on PostgreSQL; the two-term form seeks (SQL Server), looks
  up / range-scans (MySQL) and index-scans (PostgreSQL), as `Code = @p` did (`Task873IndexPlanTests`). Casting the
  column to `NVARCHAR(MAX)` for every list made SQL Server's `IN` a per-row LOB scan (1000 values over 20,000 rows: 18 s,
  was 37 ms); without it 67 ms, `NOT IN` 931 ms against 883 ms (`Contract_A_long_list_…_stays_fast`). A `Condition` built
  by hand keeps `=` and the collation. For a case-insensitive match, say so: `string.Equals(x.Code, v,
  StringComparison.OrdinalIgnoreCase)`. Orderings of strings (`string.Compare`, `CompareTo`, `CompareOrdinal`) stay
  refused. A string list past the provider's parameter limit is still refused rather than split (TASK-833's decision).
rameter limit is
  still refused rather than split (TASK-833's decision).
- **Two columns compare** (`x.A == x.B`, `x.Opened < x.Closed`); `==` / `!=` on nullable columns treat two nulls
  as equal, as C# does. `x.Opened.Date == x.Closed.Date` throws `NotSupportedException` (no portable SQL form).
- **Decimal division (TASK-870), in filters and update SET values alike.** A division by a constant whose reciprocal
  terminates (`/ 2m`, `/ 4m`, `/ 2.5m`, `/ 100m`: ±2^i·5^j·10^k, reciprocal of at most 10 digits) is rendered as the
  product with that reciprocal (`x.Amount * 0.25`), which is exact on every provider. Any other decimal division (by `3m`,
  by a column) keeps C#'s 28 to 29 significant digits only on SQLite, which divides in C#'s decimal; each server keeps its
  own scale of the quotient (SQL Server as few as six decimals, MySQL four more than the dividend, PostgreSQL about
  sixteen significant digits), so it throws `NotSupportedException` there before anything runs. A decimal `%`
  translates everywhere.
- **Division by zero (TASK-888), in filters, update SET values and `PropertyUpdate.Compute`.** A zero divisor of `/` or
  `%` fails the statement on every provider, and nothing is written; a NULL divisor gives NULL, as C#'s lifted operators
  do. C# throws `DivideByZeroException` for an integer or a decimal. For a `double` C# gives Infinity or NaN, which no
  provider can compute, so it fails too (a refusal, never another answer). What the caller sees is the provider's own
  exception: PostgreSQL `PostgresException` 22012, SQL Server `SqlException` 8134, SQLite `SqliteException` (from the
  registered `birko_divisor`, with a `DivideByZeroException` message), MySQL `MySqlException` 1411 naming
  "Division by zero in a filter or SET value" (MySQL has no error-raising expression, so the divisor is a `CASE` whose
  zero branch raises). Before, SQLite and MySQL computed NULL: a read lost the row and a count left it out, silently.
- **Update SET values (TASK-870).** `x.At.Date` renders `CAST(… AS DATE)` (SQL Server has no `DATE()`), and on SQLite
  midnight in the text a DateTime parameter binds as. `Replace(a, b)` matches ordinally, as C#: under SQL Server's
  default collation it matched case-insensitively and by linguistic rules (`ß` = `ss`), so the column is compared in
  `Latin1_General_100_BIN2`; a null `b` removes, as in C#; an empty, null or row-read `a` throws
  `NotSupportedException`, since C# throws for an empty one. On SQLite an integer SET (`Hits = Hits + 1`,
  `Increment`) that leaves its C# type's range fails the statement (`birko_checked_int`) instead of storing a value
  every later read of the row throws on; the servers already failed it.
- **Integer overflow (TASK-890), in filters and update SET values alike.** Integer arithmetic (`+ - * / %`, unary
  minus, `Math.Abs`) is computed in the C# type of the expression, and a result outside that type's range **fails the
  statement on every provider**: C# `checked` semantics, not C#'s default wrap, which no provider computes. A read whose
  arithmetic overflows on any row fails as a whole, and a write writes nothing. Arithmetic that does not overflow
  answers what C# answers.
  - MySQL computes integers in `BIGINT`, so an `int` result is range-checked as `((x * 2^32) DIV 2^32)` (MySQL error
    1690 when it does not fit). Before, `x.Hits * 2 / 2 == x.Hits` matched the `int.MaxValue` row, and
    `SET Hits = Hits * 2 / 2` stored it unchanged, silently.
  - PostgreSQL and SQL Server compute in the column's own type, so an integer widened in C# (`x.Sh * x.Sh`, `-x.Sh`,
    `(long)x.Hits * 2`) is cast to `INTEGER` / `BIGINT`. Before, they failed with an overflow C# never has.
  - A SET narrowed to its column's type (`Sh = (short)(Sh + 1)`) translates on every provider, SQLite included (refused
    there from TASK-860 to TASK-890), and fails when the value does not fit.
  - What the caller sees is the provider's own exception, as for a division by zero: PostgreSQL `PostgresException`
    22003, SQL Server `SqlException` 8115 / 220, MySQL `MySqlException` 1690 / 1264, SQLite `SqliteException` (error 1,
    from `birko_checked_int`, with the overflow in its message). All four are `DbException`, which is the one type to
    catch. They are not rewrapped as `OverflowException`: that would drop each driver's error code that callers and the
    retry filter select on, and would earn no catch `DbException` does not already give (rule 9).
- Predicates also accept ternaries, `??`, and column arithmetic — see
  [CLAUDE.md § Filter translation](CLAUDE.md) for the full supported surface.

## License

Part of the Birko Framework.
