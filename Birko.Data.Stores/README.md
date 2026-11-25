# Birko.Data.Stores

Store abstractions for the Birko Framework. Contains store interfaces, abstract implementations, ordering, aggregation, and service locator. Settings hierarchy (Settings, PasswordSettings, RemoteSettings) is provided by `Birko.Settings` and imported transitively.

## Store Hierarchy

```
AbstractStore<T>
    -> AbstractBulkStore<T> (sync, with ordering/paging)

AbstractAsyncStore<T>
    -> AbstractAsyncBulkStore<T> (async, with ordering/paging)
```

## Settings Chain

```
Settings (Location, Name)
    -> PasswordSettings (Password)
        -> RemoteSettings (UserName, Port, UseSecure)
```

## Key Interfaces

- `IStore<T>` / `IAsyncStore<T>` — Single-entity CRUD operations
- `IBulkStore<T>` / `IAsyncBulkStore<T>` — Batch operations with filtering, ordering, paging, filter-based update/delete
- `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` — Optional server-side aggregation (GROUP BY, SUM, AVG, MIN, MAX, COUNT with time bucketing). Names are property (or column) names, refused when they resolve to nothing; on SQL stores only TimescaleDB buckets by time, other SQL providers refuse it (TASK-706)
- `PropertyUpdate<T>` — Fluent builder for native partial property updates (SQL SET, MongoDB $set, ES scripts)
- `IStoreWrapper` — Decorator pattern for store composition
- `ITransactionalStore<T, TContext>` — External transaction participation

## Filter-Based Bulk Operations

Bulk stores support three patterns for update/delete by filter:

```csharp
// 1. PropertyUpdate — native platform operation (single SQL UPDATE SET WHERE)
store.Update(
    x => x.Category == "old",
    new PropertyUpdate<Product>().Set(x => x.Active, false).Set(x => x.Category, "archived")
);

// 1b. Increment / Decrement — counter in the same statement (col = col + delta); atomic on SQL and MongoDB
store.Update(
    x => x.Guid == id,
    new PropertyUpdate<Redirect>().Increment(x => x.HitCount, 1).Set(x => x.LastHitAt, now)
);

// 1c. Compute — a decimal column from other columns of the same row, in the same statement (TASK-826)
store.Update(
    x => x.TaxRateGuid == rateGuid,
    new PropertyUpdate<BasePrice>()
        .Set(x => x.TaxRatePercent, newRate)
        .Compute(x => x.PriceWithoutTax, x => Math.Round(x.PriceWithTax / (1 + newRate / 100m), 2, MidpointRounding.ToEven))
);

// 2. Action<T> — read-modify-save (for complex mutations)
store.Update(x => x.Price > 100, item => { item.Price *= 0.9m; });

// 3. Delete by filter — native platform operation (single SQL DELETE WHERE)
store.Delete(x => x.IsExpired);
```

| Platform | PropertyUpdate | Increment | Compute | Delete(filter) |
|----------|---------------|-----------|---------|----------------|
| SQL | Native `UPDATE SET WHERE` | `col = col + @p`, same statement | the formula in SQL, same statement | Native `DELETE WHERE` |
| MongoDB | `UpdateMany` with `$set` | `$inc` | one pipeline update (`$set` with `$divide` / `$round` …) | `DeleteMany` |
| ElasticSearch | `UpdateByQuery` (Painless) | `ctx._source.f += params.p` — a version conflict aborts the write part-way and throws `StoreConcurrencyException` (TASK-502) | Painless `BigDecimal` (same check) | `DeleteByQuery` (same check) |
| RavenDB | One server-side patch per document, all in one transaction, for sets of non-floating values (TASK-827); a decimal / double / float value, or inside a unit of work: fallback | Server-side for `short` / `int` / `long` (a `long` past 2^53 is refused: patch JS numbers are doubles); `decimal` / `double` / `float`: fallback | Fallback in C# — **not atomic** | Fallback read-then-delete |
| Others | Fallback read-modify-save | Fallback — **not atomic** | Fallback in C# — **not atomic** | Fallback read-then-delete |

**Increment / Decrement** (`short`, `int`, `long`, `float`, `double`, `decimal` properties — the types every provider
stores as a number; `Decrement` is `Increment` with a negated delta). Nullable properties do not compile; a cast in
the selector (`x => (int)x.MaybeCount`) or a nested member (`x => x.Stats.Count`) is refused — `NULL + 1` means
something different on every backend, and providers resolve only the leaf of a nested path. An increment cannot share
a `PropertyUpdate` with another assignment to the same property. Behind the localization wrappers an increment on a
localizable field is refused, and so is any increment in an update that also sets a localizable field on a
non-default culture (that update is replayed as read-modify-save). Caveats, measured or read from the providers:

**Compute** (TASK-826) sets a `decimal` (or `decimal?`) property from a formula over the same row: non-nullable
`decimal` properties, `decimal` constants and captured values (evaluated once, when added), `+ - * /`, unary minus, and
`Math.Round` / `decimal.Round` **with an explicit** `MidpointRounding.ToEven` or `AwayFromZero`. Anything else is
refused when added; so is a `Math.Round` without a mode, because C# and MongoDB round half to even by default and SQL
half away from zero. Every provider stores what C# computes on the same row (a cross-provider contract at rounding
midpoints, both modes, all 13 providers): SQL Server, PostgreSQL and MySQL emulate half-to-even around `ROUND`, MongoDB
emulates away-from-zero around `$round`, SQLite computes in its exact decimal functions. The formula reads the row as it
was before the update and may not read a column the same update assigns (refused: MySQL evaluates a SET list left to
right, the others against the old row). A division by zero fails the write on every provider. On InfluxDB a `decimal`
is stored as a double, as for every write there.

- **SQLite:** a `decimal` is `TEXT COLLATE BIRKO_DECIMAL` and increments through `birko_decimal_add`, so
  `10.10m + 0.20m` is exactly `10.30m` and an increment past `decimal.MaxValue` is refused (TASK-513; before it,
  `REAL`/`NUMERIC` stored a float and the counter drifted to `10.299999999999999m`). A declared precision / scale
  is not enforced there — `1.2345678` stays `1.2345678` where `DECIMAL(22,6)` elsewhere rounds it (TASK-515). An
  integer that overflows becomes a `REAL` instead of raising, where the other providers and the fallback throw.
- **MySQL / MSSql:** a `decimal` without declared precision is `DECIMAL(22,6)` (TASK-512; it was an integer column
  before) — declare `[PrecisionField]` / `[ScaleField]` where six decimal places are not enough.
- **MongoDB:** a `decimal` is stored as Decimal128 by default (measured, MongoDB.Bson 3.12) and increments exactly.
  A member opted into string storage (`[BsonRepresentation(BsonType.String)]`) is refused up front, since `$inc` on
  a string fails at the server.
- **ElasticSearch:** AutoMap maps `decimal` to `double`, so a decimal increment runs in double arithmetic.

## Aggregation

Stores can optionally implement `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` for server-side aggregation:

```csharp
var query = new AggregateQuery<Order>
{
    Filter = o => o.Status == OrderStatus.Completed,
    GroupByFields = ["CustomerId"],
    Aggregates =
    [
        new AggregateField(AggregateFunction.Count, "Id", "order_count"),
        new AggregateField(AggregateFunction.Sum, "Total", "total_spent"),
    ],
    TimeBucketInterval = "1 hour",   // optional time bucketing
    TimeColumn = "CreatedAt",
    Limit = 10,
    // StringComparison = StringComparison.Ordinal (the default): the order of a string Min / Max.
    // Another order is answered in memory and refused by name by a provider that cannot sort that way.
};

IReadOnlyList<AggregateResult> results = await store.AggregateAsync(query);
foreach (var row in results)
{
    var count = row.GetValue<int>("order_count");
    var total = row.GetValue<decimal>("total_spent");
}
```

**Key types:** `AggregateFunction` (enum), `AggregateField` (record), `AggregateQuery<T>`, `AggregateResult`, `AggregateHelper` (LINQ fallback), `TimeIntervalParser`, `OrderByHelper`.

## Choosing the backend by configuration (TASK-990)

`IStoreFactory` hands out async stores without the code naming its backend, SQL or not, so the same code runs on
InMemory or SQLite in tests and on PostgreSQL, MongoDB or Cosmos DB in production. The backend is a name plus a
connection string; each backend project registers itself, so an app carries only the drivers it adds:

```csharp
IStoreFactory db = StoreFactory.Create(cfg.Backend, cfg.ConnectionString,
    p => p.AddInMemory().AddSqLite().AddPostgreSQL().AddMongoDB());
services.AddSingleton(db);

if (db is ISchemaStoreFactory schema) await schema.CreateSchemaAsync(new[] { typeof(Order) });  // SQL tables
IAsyncBulkStore<Order> orders = db.GetAsyncStore<Order>();
var repository = db.GetAsyncRepository<Order>();           // Birko.Data.Repositories: AsyncStoreRepository<Order>
if (orders is IAsyncAggregatableStore<Order> aggregates) { /* … */ }

// Or from the backend's own settings object; its type picks the backend.
IStoreFactory mongo = StoreFactory.Create(new Birko.Data.MongoDB.Stores.Settings("db-host", "app"), p => p.AddMongoDB());
```

| Backend name | Registered by | Connection string |
|---|---|---|
| `InMemory` | `AddInMemory()` | none (empty) |
| `Json` / `Xml` | `AddJson()` / `AddXml()` | a folder (`data/store`) or `Path=…`; each type is its own file, `{full type name}.json` / `.xml` |
| `SqLite`, `PostgreSQL`, `MySQL`, `MSSql` | `AddSqLite()`, … | as for [`SqlStoreFactory`](../Birko.Data.SQL/README.md) |
| `MongoDB` | `AddMongoDB()` | MongoDB's own, `mongodb://…/database` or `mongodb+srv://…/database`, passed to the driver verbatim |
| `ElasticSearch` | `AddElasticSearch()` | `Url=http://host:9200;IndexPrefix=app`; each type is the index `{prefix}_{type}` |
| `RavenDB` | `AddRavenDB()` | `Url=http://host:8080;Database=app` (a client certificate goes on a `DocumentStore`, not in a string) |
| `CosmosDB` | `AddCosmosDB()` | Cosmos DB's own `AccountEndpoint=…;AccountKey=…` plus `Database=…`, optionally `ConnectionMode=Gateway`; each type is its own container |

- **Refused by name, never dropped:** an unregistered backend (the message lists the registered ones), a name registered
  twice, a missing required part, and any key the backend's settings cannot carry (rule 41).
- **One store per type** for every non-SQL backend: InMemory, JSON and XML keep the data in the store object (two
  instances would not see each other's writes), and MongoDB, RavenDB and Cosmos DB stores each build their own client.
  A SQL backend answers a new store each time over its cached connector.
- **`Create(settings, …)`** picks the backend registered for the settings' type, or for a backend settings type it
  derives from (TimescaleDB's settings pick PostgreSQL). The shared base types in `Birko.Configuration` (`Settings`,
  which InMemory, JSON and XML all take) match only exactly, and are refused when more than one registered backend takes
  them: use the string form there.
- **Schema:** `ISchemaStoreFactory` is a capability to check for. The SQL backends have it; the NoSQL ones create their
  database, index or container on first use and do not claim it.
- InfluxDB is not a backend here yet: its point layout is still open (TASK-910).
- Names are matched ignoring case. Declare a namespace other than `Birko.Data.StoreFactory` in your own code, or write
  `Birko.Data.Stores.StoreFactory`: inside `Birko.Data.*` a namespace of that name hides the class.

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
```

**Note:** Birko.Data.Stores requires Birko.Data.Core. Settings classes are imported transitively from Birko.Settings (namespace `Birko.Configuration`).

## License

MIT License - see [License.md](License.md)
