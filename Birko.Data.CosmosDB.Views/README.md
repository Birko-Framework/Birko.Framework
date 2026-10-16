# Birko.Data.CosmosDB.Views

Azure Cosmos DB (NoSQL API) platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Provides read-only view queries over Cosmos DB containers using a Cosmos SQL projection for non-aggregate queries and Cosmos SQL with GROUP BY for aggregate queries.

## Components

- **CosmosViewStore\<TView\>** — Implements `IViewStore<TView>`. Non-aggregate views SELECT each source field under its view property's name, with WHERE/ORDER BY/OFFSET LIMIT mapped to the source fields. Aggregate views use Cosmos SQL with GROUP BY via `QueryDefinition` and `GetItemQueryIterator<TView>`. Aggregate SQL built via shared `CosmosAggregationHelper`. Uses `OrderByHelper.ApplyTo()` for dynamic ordering. Joins and Persistent mode are refused: Cosmos DB has no cross-document joins and no stored views.
- **CosmosViewManager** — Implements `IViewManager`. `EnsureAsync` refuses a Persistent definition or a join and otherwise creates nothing. `DropAsync` never deletes a container and `ExistsAsync` is always false: no container is a view. `RefreshAsync` is a no-op (views are computed on-the-fly).
- **CosmosFilterTranslator** — Translates `Expression<Func<T, bool>>` filters into Cosmos SQL WHERE clauses. Supports binary comparisons, logical AND/OR, NOT, and string Contains.

## Query Translation

| ViewDefinition | Cosmos DB |
|---|---|
| From | Container |
| Select (no agg) | `SELECT c["Source"] AS ViewProp` |
| GroupBy + Aggregates | Cosmos SQL `GROUP BY` (via CosmosAggregationHelper) |
| Count/Sum/Avg/Min/Max | Cosmos SQL aggregate functions |
| OrderBy | `OrderByHelper.ApplyTo()` |
| Joins | Refused (`NotSupportedException`) |
| Persistent | Refused (no stored views) |

## Usage

```csharp
// Query via IViewStore
var store = new CosmosViewStore<CategorySales>(cosmosDatabase, definition);
// Top ten by an aggregate. A filter may test only group keys: an aggregate would need HAVING.
var results = await store.QueryAsync(orderBy: OrderBy<CategorySales>.ByDescending(v => v.TotalSales), limit: 10);

// View manager (limited — Cosmos DB lacks native views)
var manager = new CosmosViewManager(cosmosDatabase);
var exists = await manager.ExistsAsync("category_sales");
```

## Important Notes

- Cosmos DB does not support cross-container joins; join clauses in ViewDefinition are refused
- Cosmos DB does not support native server-side views; all queries are computed on-the-fly
- Every query is raw, parameterised Cosmos SQL
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
