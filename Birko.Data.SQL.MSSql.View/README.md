# Birko.Data.SQL.MSSql.View

SQL Server-specific view DDL support for the Birko.Data.SQL.View framework.

## Features

- **CREATE OR ALTER VIEW** syntax (requires SQL Server 2016 SP1+)
- **ViewExists** check via `sys.views` catalog
- **Indexed views** support:
  - `CreateIndexedView` (CREATE VIEW with SCHEMABINDING + clustered index)
  - `DropIndexedView` (drops clustered index then drops view)
  - `IndexedViewExists` check
  - Async variants: `CreateIndexedViewAsync`, `DropIndexedViewAsync`, `IndexedViewExistsAsync`
- Inherits all base view operations from Birko.Data.SQL.View (CreateView, DropView, RecreateView, CreateViewIfNotExists, CreateViews, DropViews)

## Usage

The MSSql connector automatically uses SQL Server-specific DDL when creating or checking views:

```csharp
// Create a persistent view
connector.CreateView(typeof(CustomerOrderView));

// Check existence via sys.views
bool exists = connector.ViewExists("customer_orders_view");

// Async equivalents
await connector.CreateViewAsync(typeof(CustomerOrderView));
bool exists = await connector.ViewExistsAsync("customer_orders_view");

// Indexed views (SQL Server only)
connector.CreateIndexedView(typeof(OrderTotalsView), "IX_OrderTotals_Clustered");
bool indexedExists = connector.IndexedViewExists("order_totals_view");
connector.DropIndexedView("order_totals_view", "IX_OrderTotals_Clustered");

// Async indexed view equivalents
await connector.CreateIndexedViewAsync(typeof(OrderTotalsView), "IX_OrderTotals_Clustered");
bool indexedExists = await connector.IndexedViewExistsAsync("order_totals_view");
await connector.DropIndexedViewAsync("order_totals_view", "IX_OrderTotals_Clustered");
```

### Indexed Views

SQL Server indexed views use `WITH SCHEMABINDING` and a unique clustered index to materialize the view results on disk. This provides significant performance improvements for aggregate queries.

```csharp
// Creates a view with SCHEMABINDING and a unique clustered index
connector.CreateIndexedView(typeof(OrderTotalsView), "IX_OrderTotals_Clustered");

// DropIndexedView drops the clustered index first, then drops the view
connector.DropIndexedView("order_totals_view", "IX_OrderTotals_Clustered");
```

## Dependencies

- Birko.Data.SQL
- Birko.Data.SQL.View
- Birko.Data.SQL.MSSql

## Related Projects

- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) - Base view framework
- [Birko.Data.SQL.MSSql](../Birko.Data.SQL.MSSql/) - SQL Server connector

## License

MIT License - Copyright 2026 Frantisek Beren
