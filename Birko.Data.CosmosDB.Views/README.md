# Birko.Data.CosmosDB.Views

Azure Cosmos DB (NoSQL API) platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Provides read-only view queries over Cosmos DB containers using LINQ for non-aggregate queries and Cosmos SQL with GROUP BY for aggregate queries.

## Components

- **CosmosViewStore\<TView\>** — Implements `IViewStore<TView>`. Non-aggregate views use LINQ via `container.GetItemLinqQueryable<T>()` with Where/OrderBy/Skip/Take. Aggregate views use Cosmos SQL with GROUP BY via `QueryDefinition` and `GetItemQueryIterator<TView>`. Aggregate SQL built via shared `CosmosAggregationHelper`. Uses `OrderByHelper.ApplyTo()` for dynamic ordering. Joins are ignored (Cosmos DB does not support cross-container joins).
- **CosmosViewManager** — Implements `IViewManager`. `EnsureAsync` is a no-op (Cosmos DB has no native server-side views). `DropAsync` deletes the named container. `ExistsAsync` checks container existence. `RefreshAsync` is a no-op (views are computed on-the-fly).
- **CosmosFilterTranslator** — Translates `Expression<Func<T, bool>>` filters into Cosmos SQL WHERE clauses. Supports binary comparisons, logical AND/OR, NOT, and string Contains.

## Query Translation

| ViewDefinition | Cosmos DB |
|---|---|
| From | Container |
| Select (no agg) | LINQ `GetItemLinqQueryable` |
| GroupBy + Aggregates | Cosmos SQL `GROUP BY` (via CosmosAggregationHelper) |
| Count/Sum/Avg/Min/Max | Cosmos SQL aggregate functions |
| OrderBy | `OrderByHelper.ApplyTo()` |
| Joins | Not supported (silently ignored) |
| Persistent | No-op (no native server-side views) |

## Usage

```csharp
// Query via IViewStore
var store = new CosmosViewStore<CategorySales>(cosmosDatabase, definition);
var results = await store.QueryAsync(v => v.TotalSales > 1000m, limit: 10);

// View manager (limited — Cosmos DB lacks native views)
var manager = new CosmosViewManager(cosmosDatabase);
var exists = await manager.ExistsAsync("category_sales");
```

## Important Notes

- Cosmos DB does not support cross-container joins; join clauses in ViewDefinition are ignored
- Cosmos DB does not support native server-side views; all queries are computed on-the-fly
- Aggregate queries build raw Cosmos SQL; non-aggregate queries use the LINQ provider
- A true materialized view would require change-feed-based pipelines (Azure Functions), which is beyond scope

## Dependencies

- [Birko.Data.Views](../Birko.Data.Views/) (IViewStore, IViewManager, ViewDefinition)
- [Birko.Data.Stores](../Birko.Data.Stores/) (OrderBy, OrderByHelper, AggregateFunction)
- [Birko.Data.CosmosDB](../Birko.Data.CosmosDB/) (CosmosAggregationHelper)
- Microsoft.Azure.Cosmos (Container, Database, QueryDefinition)

## Related Projects

- [Birko.Data.Views](../Birko.Data.Views/) — Platform-agnostic fluent view builder
- [Birko.Data.SQL.Views](../Birko.Data.SQL.Views/) — SQL platform implementation
- [Birko.Data.MongoDB.Views](../Birko.Data.MongoDB.Views/) — MongoDB platform implementation
- [Birko.Data.ElasticSearch.Views](../Birko.Data.ElasticSearch.Views/) — ElasticSearch platform implementation
- [Birko.Data.RavenDB.Views](../Birko.Data.RavenDB.Views/) — RavenDB platform implementation

## License

Part of the Birko Framework.
