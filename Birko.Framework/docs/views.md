# Views Guide

## Overview

Birko.Data.Views provides a unified fluent API for defining cross-platform views — projections, joins, and aggregations that work identically across SQL, MongoDB, ElasticSearch, RavenDB, and Cosmos DB. It replaces attribute-based SQL view definitions with a type-safe fluent builder.

## Core Concepts

### View Definition

A view is a read-only projection over one or more source entities. It can include:
- **Field selections** — pick specific properties from source entities
- **Joins** — combine multiple source entities
- **Aggregates** — Count, Sum, Avg, Min, Max
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

## See Also

- [Data Patterns Guide](patterns.md)
- [Store Implementation Guide](store-implementation.md)
