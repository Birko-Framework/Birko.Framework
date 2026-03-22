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

// Async materialized view methods
await connector.CreateMaterializedViewAsync(typeof(UserStatsView));
await connector.RefreshMaterializedViewAsync("user_stats_view", concurrently: true);
await connector.DropMaterializedViewAsync("user_stats_view");
bool matExists = await connector.MaterializedViewExistsAsync("user_stats_view");
```

### Async Materialized Views

All materialized view operations have async variants for non-blocking execution:

```csharp
// Create a materialized view asynchronously
await connector.CreateMaterializedViewAsync(typeof(UserStatsView));

// Refresh with optional concurrent mode (requires a unique index on the view)
await connector.RefreshMaterializedViewAsync("user_stats_view", concurrently: true);

// Check existence and drop
bool exists = await connector.MaterializedViewExistsAsync("user_stats_view");
await connector.DropMaterializedViewAsync("user_stats_view");
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
