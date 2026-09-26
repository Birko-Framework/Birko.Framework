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
- `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` — Optional server-side aggregation (GROUP BY, SUM, AVG, MIN, MAX, COUNT with time bucketing)
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

// 2. Action<T> — read-modify-save (for complex mutations)
store.Update(x => x.Price > 100, item => { item.Price *= 0.9m; });

// 3. Delete by filter — native platform operation (single SQL DELETE WHERE)
store.Delete(x => x.IsExpired);
```

| Platform | PropertyUpdate | Increment | Delete(filter) |
|----------|---------------|-----------|----------------|
| SQL | Native `UPDATE SET WHERE` | `col = col + @p`, same statement | Native `DELETE WHERE` |
| MongoDB | `UpdateMany` with `$set` | `$inc` | `DeleteMany` |
| ElasticSearch | `UpdateByQuery` (Painless) | `ctx._source.f += params.p` — a version conflict aborts and is not yet reported (TASK-502) | `DeleteByQuery` |
| Others | Fallback read-modify-save | Fallback — **not atomic** | Fallback read-then-delete |

**Increment / Decrement** (`short`, `int`, `long`, `float`, `double`, `decimal` properties — the types every provider
stores as a number; `Decrement` is `Increment` with a negated delta). Nullable properties do not compile; a cast in
the selector (`x => (int)x.MaybeCount`) or a nested member (`x => x.Stats.Count`) is refused — `NULL + 1` means
something different on every backend, and providers resolve only the leaf of a nested path. An increment cannot share
a `PropertyUpdate` with another assignment to the same property. Behind the localization wrappers an increment on a
localizable field is refused, and so is any increment in an update that also sets a localizable field on a
non-default culture (that update is replayed as read-modify-save). Caveats, measured or read from the providers:

- **SQLite:** a `decimal` is stored as an 8-byte float under both `REAL` and `NUMERIC(p,s)`, so `10.10m + 0.20m`
  reads back as `10.299999999999999m` (a Set of `10.30m` round-trips exactly). A decimal counter drifts there. An
  integer that overflows becomes a `REAL` instead of raising, where the other providers and the fallback throw.
- **MySQL / MSSql:** a `decimal` without declared precision is `DECIMAL(10,0)` / `DECIMAL(18,0)` and loses its
  fraction on every write, increment or not — declare `[PrecisionField]` / `[ScaleField]`.
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
    Limit = 10
};

IReadOnlyList<AggregateResult> results = await store.AggregateAsync(query);
foreach (var row in results)
{
    var count = row.GetValue<int>("order_count");
    var total = row.GetValue<decimal>("total_spent");
}
```

**Key types:** `AggregateFunction` (enum), `AggregateField` (record), `AggregateQuery<T>`, `AggregateResult`, `AggregateHelper` (LINQ fallback), `TimeIntervalParser`, `OrderByHelper`.

## Usage

This is a shared project (`.shproj`). Import it in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Core\Birko.Data.Core.projitems" Label="Shared" />
<Import Project="..\Birko.Data.Stores\Birko.Data.Stores.projitems" Label="Shared" />
```

**Note:** Birko.Data.Stores requires Birko.Data.Core. Settings classes are imported transitively from Birko.Settings (namespace `Birko.Configuration`).

## License

MIT License - see [License.md](License.md)
