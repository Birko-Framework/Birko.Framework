# Views Guide

## Overview

Birko.Data.Views provides a unified fluent API for defining cross-platform views — projections, joins, and aggregations that work identically across SQL, MongoDB, ElasticSearch, RavenDB, and Cosmos DB. It replaces attribute-based SQL view definitions with a type-safe fluent builder.

## Project Hierarchy

```
Birko.Data.Stores             — Shared aggregation types (AggregateFunction,
│                               AggregateQuery, AggregateResult, OrderByHelper,
│                               TimeIntervalParser, AggregateHelper)
│
└─► Birko.Data.Views          — Platform-agnostic: fluent API (ViewDefinitionBuilder,
    │                           ViewMapRegistry, IViewStore, IViewManager)
    │
    ├─► Birko.Data.SQL.Views  — SQL bridge: translates ViewDefinition → Tables.View
    │   │                       (SqlViewTranslator, SqlViewStore, SqlViewManager)
    │   │
    │   └─► Birko.Data.SQL.View — SQL engine: attribute-based definitions, connector
    │       │                      extensions, DDL, field types, query building
    │       │
    │       ├─► Birko.Data.SQL.MSSql.View
    │       ├─► Birko.Data.SQL.PostgreSQL.View
    │       ├─► Birko.Data.SQL.MySQL.View
    │       └─► Birko.Data.SQL.SqLite.View
    │
    ├─► Birko.Data.MongoDB.Views  — MongoDB (aggregation pipelines, StoreAggregationHelper)
    ├─► Birko.Data.ElasticSearch.Views — ElasticSearch (NEST aggs, StoreAggregationHelper)
    ├─► Birko.Data.RavenDB.Views  — RavenDB (Map/Reduce static indexes)
    └─► Birko.Data.CosmosDB.Views — Cosmos DB (Cosmos SQL, CosmosAggregationHelper)
```

- **Birko.Data.Stores** defines shared aggregation types (`AggregateFunction`, `AggregateQuery`, `AggregateResult`, helpers).
- **Birko.Data.Views** defines *what* a view is (interfaces, fluent builder, registry). Uses `AggregateFunction` from Stores.
- **Birko.Data.SQL.Views** translates the fluent definitions into SQL metadata (`Tables.View`). Uses `AbstractConnectorBase.GetSqlFunctionName()` and `FunctionField.CreateFunctionField()`.
- **Birko.Data.SQL.View** executes the SQL work (DDL, SELECT building, connector extensions).
- **Platform view projects** use shared helpers (`OrderByHelper`, platform-specific `StoreAggregationHelper` / `CosmosAggregationHelper`) to translate aggregation into native operations.
- **Provider-specific projects** supply database-dialect SQL (e.g. `CREATE MATERIALIZED VIEW` for PostgreSQL).

## Core Concepts

### View Definition

A view is a read-only projection over one or more source entities. It can include:
- **Field selections** — pick specific properties from source entities
- **Joins** — combine multiple source entities
- **Aggregates** — Count, Sum, Avg, Min, Max (via `AggregateFunction` from `Birko.Data.Stores`)
- **GroupBy** — group aggregation results

### Query Modes

| Mode | Behavior |
|------|----------|
| `OnTheFly` | Query is computed at runtime (SELECT+JOINs, aggregation pipeline, etc.) |
| `Persistent` | Query runs against a pre-created view (SQL VIEW, MongoDB view, RavenDB static index) |
| `Auto` | Tries persistent first, falls back to on-the-fly |

## Defining Views

### 1. Create the view result type

Plain class with no attributes needed:

```csharp
public class CustomerOrderSummary
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
}
```

### 2. Create the mapping

Implement `IViewMapping<TView>` (follows the `IModelMapping<T>` pattern):

```csharp
public class CustomerOrderSummaryMapping : IViewMapping<CustomerOrderSummary>
{
    public void Configure(ViewDefinitionBuilder<CustomerOrderSummary> builder)
    {
        builder
            .HasName("customer_order_summary")
            .HasQueryMode(ViewQueryMode.Persistent)
            .From<Customer>()
            .LeftJoin<Customer, Order, Guid>(c => c.Guid!.Value, o => o.CustomerId)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Count<Order>(v => v.OrderCount)
            .Sum<Order, decimal>(o => o.Total, v => v.TotalSpent);
    }
}
```

### 3. Register at startup

```csharp
var registry = new ViewMapRegistry();

// Register individual mapping
registry.Register(new CustomerOrderSummaryMapping());

// Or scan an entire assembly
registry.RegisterFromAssembly(typeof(CustomerOrderSummaryMapping).Assembly);

// Retrieve definition
var definition = registry.GetDefinition<CustomerOrderSummary>();
```

## Builder API

### Source & Fields

```csharp
builder
    .From<Product>()                                          // primary source
    .Select<Product, string>(p => p.Name, v => v.ProductName) // field selection
    .Select<Product, decimal>(p => p.Price, v => v.Price);
```

### Joins

```csharp
builder
    .From<Order>()
    .Join<Order, Customer, Guid>(o => o.CustomerId, c => c.Guid!.Value)           // INNER
    .LeftJoin<Order, Shipping, Guid>(o => o.Guid!.Value, s => s.OrderId);         // LEFT OUTER
```

### Aggregates with GroupBy

```csharp
builder
    .From<OrderLine>()
    .Join<OrderLine, Product, Guid>(ol => ol.ProductId, p => p.Guid!.Value)
    .Select<Product, string>(p => p.Category, v => v.Category)
    .GroupBy<Product, string>(p => p.Category)
    .Count<OrderLine>(v => v.LineCount)
    .Sum<OrderLine, decimal>(ol => ol.Amount, v => v.TotalAmount)
    .Avg<OrderLine, decimal>(ol => ol.Amount, v => v.AvgAmount)
    .Min<OrderLine, decimal>(ol => ol.Amount, v => v.MinAmount)
    .Max<OrderLine, decimal>(ol => ol.Amount, v => v.MaxAmount);
```

### Platform Hints

```csharp
builder
    .Hint("MaterializedViewType", "PostgreSqlMaterialized")
    .Hint("PartitionKey", "/tenantId");
```

## Querying Views

### IViewStore

Read-only store for executing view queries:

```csharp
// Get all results
var items = await viewStore.QueryAsync();

// Filter + sort + paginate
var page = await viewStore.QueryAsync(
    filter: v => v.TotalSpent > 1000m,
    orderBy: OrderBy<CustomerOrderSummary>.ByDescending(v => v.TotalSpent),
    limit: 20,
    offset: 0);

// Single result
var top = await viewStore.QueryFirstAsync(v => v.CustomerId == customerId);

// Count
var count = await viewStore.CountAsync(v => v.OrderCount > 5);
```

### IViewManager

Lifecycle management for persistent views:

```csharp
// Create persistent view if it doesn't exist
await viewManager.EnsureAsync(definition);

// Check existence
var exists = await viewManager.ExistsAsync("customer_order_summary");

// Refresh materialized data (PostgreSQL REFRESH MATERIALIZED VIEW, etc.)
await viewManager.RefreshAsync("customer_order_summary");

// Drop
await viewManager.DropAsync("customer_order_summary");
```

## Build Validation

`ViewDefinitionBuilder.Build()` validates:
- `From<T>()` must be called (primary source required)
- `HasName()` required for Persistent/Auto modes
- All non-aggregate Select fields must appear in GroupBy when aggregates are present
- Sum/Avg target properties must be numeric types

## Platform Translation

| ViewDefinition | SQL | MongoDB | ElasticSearch | RavenDB | Cosmos DB |
|---|---|---|---|---|---|
| From | FROM table | collection | index | collection | container |
| Join | JOIN ON | $lookup + $unwind | nested/parent-child | Include | N/A |
| Select | SELECT fields | $project | _source filter | Select() | SELECT |
| GroupBy | GROUP BY | $group | agg buckets | Reduce grouping | GROUP BY |
| Count/Sum/Avg | SQL functions | $sum/$avg in $group | agg metrics | Reduce functions | aggregate functions |
| Persistent | CREATE VIEW | db.createView | transform | static index | N/A |

## Migration from SQL.View Attributes

The fluent builder coexists with existing attribute-based views:
1. SQL platform translates `ViewDefinition` → `Tables.View` (same metadata attributes produce)
2. Existing connector infrastructure works unchanged
3. Migrate views incrementally: create `IViewMapping` implementations, remove attributes
4. Mark old attributes `[Obsolete]` after migration

## Store-Level Aggregation

In addition to view-based aggregation, stores can implement `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` (defined in `Birko.Data.Stores`) for direct server-side aggregation without defining a view:

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
    OrderBy = new Dictionary<string, bool> { ["total_spent"] = false },
    Limit = 10
};

IReadOnlyList<AggregateResult> results = await store.AggregateAsync(query);
foreach (var row in results)
{
    var customerId = row.GetValue<Guid>("CustomerId");
    var count = row.GetValue<int>("order_count");
    var total = row.GetValue<decimal>("total_spent");
}
```

`AggregateHelper.LinqAggregateAsync()` provides an in-memory LINQ fallback for stores that don't implement native aggregation.

## See Also

- [Data Patterns Guide](patterns.md)
- [Store Implementation Guide](store-implementation.md)
