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
// Top ten by an aggregate. A filter may test only group keys: an aggregate would need HAVING.
var results = await store.QueryAsync(orderBy: OrderBy<CustomerOrderSummary>.ByDescending(v => v.TotalSpent), limit: 10);

// Manage persistent views via IViewManager
var manager = new SqlViewManager(database);
await manager.EnsureAsync(definition);
await manager.RefreshAsync("customer_order_summary");
```

## Aggregates

A view's aggregate is rendered by the provider's dialect (`SqlValueFragmentDialect.Aggregate`) and read by
`AggregateValue.Convert`, the producer and the rule the store's `Aggregate` uses (TASK-892). It answers what LINQ answers
over the group's rows:

- A SUM over only NULLs is 0 (`COALESCE(SUM(x), 0)`), as LINQ's `Sum` answers for `int?` as for `int`. MIN, MAX and an
  average over only NULLs stay null, as in LINQ.
- An average of an integral column is a double average (PostgreSQL: `CAST(x AS DOUBLE PRECISION)`).
- A decimal SUM / AVG on SQLite runs `birko_decimal_sum` / `birko_decimal_avg` and orders by the decimal collation.
- A decimal average, or an average into a `decimal` property, is refused by name on PostgreSQL, MySQL and SQL Server
  (`NotSupportedException`, before anything runs): a view column is one value, and each server keeps its own scale
  (SQL Server 6 decimals). Read SUM and COUNT into two properties, average into a `double`, or use the store's
  `Aggregate`, which divides them exactly.
- The migration generator (`ViewSqlGenerator`) knows no provider and renders the portable form.

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
