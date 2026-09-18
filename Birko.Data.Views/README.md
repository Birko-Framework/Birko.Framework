# Birko.Data.Views

Unified fluent view builder for the Birko Framework. Define views once with a fluent API, execute on any platform.

## Features

- Fluent view definition builder (replaces attribute-based SQL view definitions)
- Cross-platform: same definition works on SQL, MongoDB, ElasticSearch, RavenDB, Cosmos DB
- Field selection, joins, aggregates (Count, Sum, Avg, Min, Max via `AggregateFunction` from `Birko.Data.Stores`), GroupBy
- Three query modes: OnTheFly, Persistent (materialized), Auto
- IViewMapping interface + ViewMapRegistry for assembly scanning
- Read-only IViewStore for querying, IViewManager for persistent view lifecycle
- Platform-specific hints for native optimizations

## Usage

```csharp
// 1. Define view result type (plain class)
public class CustomerOrderSummary
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
}

// 2. Define mapping
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

// 3. Register
var registry = new ViewMapRegistry();
registry.RegisterFromAssembly(typeof(CustomerOrderSummaryMapping).Assembly);

// 4. Query (same API regardless of platform)
var results = await viewStore.QueryAsync(v => v.TotalSpent > 1000m, limit: 10);
```

## Platform Translations

| ViewDefinition | SQL | MongoDB | ElasticSearch | RavenDB |
|---|---|---|---|---|
| From | FROM table | collection | index | collection |
| Join | JOIN ON | $lookup | nested query | Include |
| Select | SELECT fields | $project | _source filter | Select() |
| GroupBy | GROUP BY | $group | agg buckets | Reduce |
| Aggregates | SUM/COUNT/AVG | $sum/$avg | agg metrics | Reduce functions |
| Persistent | CREATE VIEW | db.createView | transform | static index |

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (OrderBy\<T\>, AggregateFunction)
