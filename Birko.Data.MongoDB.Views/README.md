# Birko.Data.MongoDB.Views

MongoDB platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Translates portable `ViewDefinition` into MongoDB aggregation pipeline stages and MongoDB views.

## Components

- **MongoViewTranslator** — Converts `ViewDefinition` → `List<BsonDocument>` pipeline stages (`$lookup`, `$unwind`, `$group`, `$project`). Group stage built via shared `StoreAggregationHelper.BuildGroupStageFromPaths()`
- **MongoViewStore\<TView\>** — Implements `IViewStore<TView>` by executing aggregation pipelines. Supports Persistent (MongoDB view collection), OnTheFly (pipeline on source collection), and Auto modes
- **MongoViewManager** — Implements `IViewManager` using MongoDB `db.createView()` command (requires MongoDB 3.4+)

## Pipeline Translation

| ViewDefinition | MongoDB Stage |
|---|---|
| Join | `$lookup` + `$unwind` |
| GroupBy + Aggregates | `$group` (via StoreAggregationHelper) |
| Select fields | `$project` |
| Filter (at query time) | `$match` |
| OrderBy | `$sort` |
| Limit/Offset | `$limit` / `$skip` |

## Usage

```csharp
// Translate and query
var store = new MongoViewStore<CustomerOrderSummary>(mongoDatabase, definition);
var results = await store.QueryAsync(v => v.TotalSpent > 1000m, limit: 10);

// Manage persistent MongoDB views
var manager = new MongoViewManager(mongoDatabase);
await manager.EnsureAsync(definition);
```

## Dependencies

- [Birko.Data.Views](../Birko.Data.Views/) (ViewDefinition, IViewStore, IViewManager)
- [Birko.Data.Stores](../Birko.Data.Stores/) (AggregateFunction, OrderByHelper)
- [Birko.Data.MongoDB](../Birko.Data.MongoDB/) (MongoDBClient, StoreAggregationHelper)
- MongoDB.Driver, MongoDB.Bson

## Related Projects

- [Birko.Data.Views](../Birko.Data.Views/) — Platform-agnostic fluent view builder
- [Birko.Data.SQL.Views](../Birko.Data.SQL.Views/) — SQL platform implementation
- [Birko.Data.ElasticSearch.Views](../Birko.Data.ElasticSearch.Views/) — ElasticSearch platform implementation
- [Birko.Data.RavenDB.Views](../Birko.Data.RavenDB.Views/) — RavenDB platform implementation
- [Birko.Data.CosmosDB.Views](../Birko.Data.CosmosDB.Views/) — Cosmos DB platform implementation

## License

Part of the Birko Framework.
