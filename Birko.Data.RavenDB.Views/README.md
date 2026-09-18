# Birko.Data.RavenDB.Views

RavenDB platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Translates portable `ViewDefinition` into RavenDB static indexes (Map/Reduce) and provides query access via `IViewStore<TView>`.

## Components

- **RavenViewTranslator** — Converts `ViewDefinition` into RavenDB Map/Reduce LINQ strings. Map uses `from entity in docs.{Collection} select new { ... }` with `LoadDocument<T>()` for joins. Reduce groups results and applies Sum/Count/Min/Max/Avg aggregates. Non-aggregate views produce Map only (no Reduce).
- **RavenViewStore\<TView\>** — Implements `IViewStore<TView>`. OnTheFly mode queries the collection directly via `session.Query<TView>()`. Persistent/Auto mode queries a static index via `session.Query<TView>(indexName)`. Uses shared `OrderByHelper.ApplyTo()` for dynamic ordering.
- **RavenViewManager** — Implements `IViewManager`. `EnsureAsync` creates a static index via `PutIndexesOperation`. `DropAsync` removes via `DeleteIndexOperation`. `ExistsAsync` checks via `GetIndexOperation`. `RefreshAsync` is a no-op (RavenDB indexes are auto-maintained).

## Index Translation

| ViewDefinition | RavenDB |
|---|---|
| From | `from entity in docs.{Collection}` |
| Join | `LoadDocument<T>()` |
| GroupBy + Aggregates | Reduce step with grouping |
| Select | Map projection fields |
| Persistent | Static index via `PutIndexesOperation` |
| OrderBy | `OrderByHelper.ApplyTo()` at query time |

## Usage

```csharp
// Query via IViewStore
var store = new RavenViewStore<CategorySales>(documentStore, definition);
var results = await store.QueryAsync(v => v.TotalSales > 1000m, limit: 10);

// Manage static indexes
var manager = new RavenViewManager(documentStore);
await manager.EnsureAsync(definition);
```

## Dependencies

- [Birko.Data.Views](../Birko.Data.Views/) (ViewDefinition, IViewStore, IViewManager)
- [Birko.Data.Stores](../Birko.Data.Stores/) (OrderBy\<T\>, OrderByHelper, AggregateFunction)
- RavenDB.Client

## Related Projects

- [Birko.Data.Views](../Birko.Data.Views/) — Platform-agnostic fluent view builder
- [Birko.Data.SQL.Views](../Birko.Data.SQL.Views/) — SQL platform implementation
- [Birko.Data.MongoDB.Views](../Birko.Data.MongoDB.Views/) — MongoDB platform implementation
- [Birko.Data.ElasticSearch.Views](../Birko.Data.ElasticSearch.Views/) — ElasticSearch platform implementation
- [Birko.Data.CosmosDB.Views](../Birko.Data.CosmosDB.Views/) — Cosmos DB platform implementation

## License

Part of the Birko Framework.
