# Birko.Data.SQL.PostgreSQL.View

PostgreSQL-specific view DDL support for the Birko.Data.SQL.View framework.

## Features

- **CREATE OR REPLACE VIEW** (native PostgreSQL syntax, uses base default)
- **ViewExists** check via `information_schema.views`
- **Materialized views** support:
  - `CreateMaterializedView` (CREATE MATERIALIZED VIEW IF NOT EXISTS)
  - `RefreshMaterializedView` with optional concurrent refresh
  - `DropMaterializedView` (DROP MATERIALIZED VIEW IF EXISTS)
- Inherits all base view operations from Birko.Data.SQL.View (CreateView, DropView, RecreateView, CreateViewIfNotExists, CreateViews, DropViews)

## Usage

```csharp
// Create a persistent view
connector.CreateView(typeof(CustomerOrderView));

// Check existence via information_schema
bool exists = connector.ViewExists("customer_orders_view");

// Materialized views (PostgreSQL only)
connector.CreateMaterializedView(typeof(UserStatsView));
connector.RefreshMaterializedView("user_stats_view", concurrently: true);
connector.DropMaterializedView("user_stats_view");

// Async equivalents
await connector.CreateViewAsync(typeof(CustomerOrderView));
bool exists = await connector.ViewExistsAsync("customer_orders_view");
```

## Dependencies

- Birko.Data.SQL
- Birko.Data.SQL.View
- Birko.Data.SQL.PostgreSQL

## Related Projects

- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) - Base view framework
- [Birko.Data.SQL.PostgreSQL](../Birko.Data.SQL.PostgreSQL/) - PostgreSQL connector

## License

MIT License - Copyright 2026 Frantisek Beren
