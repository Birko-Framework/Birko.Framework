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

// 2. Action<T> — read-modify-save (for complex mutations)
store.Update(x => x.Price > 100, item => { item.Price *= 0.9m; });

// 3. Delete by filter — native platform operation (single SQL DELETE WHERE)
store.Delete(x => x.IsExpired);
```

| Platform | PropertyUpdate | Delete(filter) |
|----------|---------------|----------------|
| SQL | Native `UPDATE SET WHERE` | Native `DELETE WHERE` |
| MongoDB | `UpdateMany` with `$set` | `DeleteMany` |
| ElasticSearch | `UpdateByQuery` (Painless) | `DeleteByQuery` |
| Others | Fallback read-modify-save | Fallback read-then-delete |

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
