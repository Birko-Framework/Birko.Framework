# Birko.Data.ElasticSearch.Views

ElasticSearch platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Translates portable `ViewDefinition` into NEST search requests with aggregations for grouped queries and `_source` filtering for simple queries.

## Components

- **ElasticSearchViewStore\<TView\>** — Implements `IViewStore<TView>` using NEST `SearchRequest`. Non-aggregate queries use `_source` filtering with sort/pagination. Aggregate queries use shared `StoreAggregationHelper` for metric creation, GROUP BY logic, and metric extraction. Joins are refused (`NotSupportedException`) rather than ignored. Filters, sorts and group keys name view properties and are mapped to the source fields they select (TASK-151).
- **ElasticSearchViewManager** — Implements `IViewManager`. `EnsureAsync` creates a destination index for persistent views (ES lacks native views; data population requires Transforms or reindex jobs). `DropAsync`/`ExistsAsync`/`RefreshAsync` map to index lifecycle operations.

## Aggregation Translation

| ViewDefinition | ElasticSearch |
|---|---|
| GroupBy (any number of keys) | `CompositeAggregation` with metric sub-aggs, read to the last page |
| Count | `ValueCountAggregation` |
| Sum/Avg/Min/Max | Corresponding metric aggregation |
| Select (no agg) | `_source` filtering, each source field projected into its view property |
| OrderBy | Sort on the selected source field (non-aggregate); in memory over the rows (aggregate) |
| Joins | Refused (`NotSupportedException`) |

## Usage

```csharp
// Query via IViewStore
var store = new ElasticSearchViewStore<CategorySales>(elasticClient, definition);
// Top ten by an aggregate. A filter may test only group keys: an aggregate would need HAVING.
var results = await store.QueryAsync(orderBy: OrderBy<CategorySales>.ByDescending(v => v.TotalSales), limit: 10);

// Manage persistent index
var manager = new ElasticSearchViewManager(elasticClient);
await manager.EnsureAsync(definition);
```

## Dependencies

- [Birko.Data.Views](../Birko.Data.Views/) (ViewDefinition, IViewStore, IViewManager)
- [Birko.Data.Stores](../Birko.Data.Stores/) (AggregateFunction, OrderByHelper)
- [Birko.Data.ElasticSearch](../Birko.Data.ElasticSearch/) (ParseExpression, StoreAggregationHelper)
- NEST (ElasticClient, SearchRequest, aggregation types)

## Related Projects

- [Birko.Data.Views](../Birko.Data.Views/) — Platform-agnostic fluent view builder
- [Birko.Data.SQL.Views](../Birko.Data.SQL.Views/) — SQL platform implementation
- [Birko.Data.MongoDB.Views](../Birko.Data.MongoDB.Views/) — MongoDB platform implementation
- [Birko.Data.RavenDB.Views](../Birko.Data.RavenDB.Views/) — RavenDB platform implementation
- [Birko.Data.CosmosDB.Views](../Birko.Data.CosmosDB.Views/) — Cosmos DB platform implementation

## License

Part of the Birko Framework.
