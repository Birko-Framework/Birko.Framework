# Birko.Data.SQL.Views

SQL platform implementation for the [Birko.Data.Views](../Birko.Data.Views/) fluent view builder. Translates portable `ViewDefinition` into SQL `Tables.View` metadata and reuses existing connector infrastructure for query execution and DDL.

## Components

- **SqlViewTranslator** — Converts `ViewDefinition` → `Tables.View` by loading source tables via `DataBase.LoadTable`, mapping fields, creating `FunctionField`s for aggregates (via `FunctionField.CreateFunctionField()` and `AbstractConnectorBase.GetSqlFunctionName()`), and building Join conditions
- **SqlViewStore\<TView\>** — Implements `IViewStore<TView>` using existing connector `SelectView`/`Select` infrastructure
- **SqlViewManager** — Implements `IViewManager` using connector `CreateView`/`DropView`/`ViewExists`

## Usage

```csharp
// Create the SQL view store using your connector and view registry
var translator = new SqlViewTranslator(database);
var definition = registry.GetDefinition<CustomerOrderSummary>();
var sqlView = translator.Translate(definition);

// Query via IViewStore
var store = new SqlViewStore<CustomerOrderSummary>(database, definition);
var results = await store.QueryAsync(v => v.TotalSpent > 1000m, limit: 10);

// Manage persistent views via IViewManager
var manager = new SqlViewManager(database);
await manager.EnsureAsync(definition);
await manager.RefreshAsync("customer_order_summary");
```

## Dependencies

- [Birko.Data.Views](../Birko.Data.Views/) (ViewDefinition, IViewStore, IViewManager)
- [Birko.Data.SQL](../Birko.Data.SQL/) (DataBase, AbstractConnector, Tables, Fields, Conditions)
- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) (Tables.View, ViewQueryMode, FunctionField)

## Related Projects

- [Birko.Data.Views](../Birko.Data.Views/) — Platform-agnostic fluent view builder
- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) — Attribute-based SQL view engine
- [Birko.Data.MongoDB.Views](../Birko.Data.MongoDB.Views/) — MongoDB platform implementation
- [Birko.Data.ElasticSearch.Views](../Birko.Data.ElasticSearch.Views/) — ElasticSearch platform implementation
- [Birko.Data.RavenDB.Views](../Birko.Data.RavenDB.Views/) — RavenDB platform implementation
- [Birko.Data.CosmosDB.Views](../Birko.Data.CosmosDB.Views/) — Cosmos DB platform implementation

## License

Part of the Birko Framework.
