# Birko.Data.SQL.SqLite.View

SQLite-specific view DDL support for the Birko.Data.SQL.View framework.

## Features

- **CREATE VIEW IF NOT EXISTS** syntax (SQLite does not support CREATE OR REPLACE VIEW)
- **ViewExists** check via `sqlite_master` catalog
- Inherits all base view operations from Birko.Data.SQL.View (CreateView, DropView, RecreateView, CreateViewIfNotExists, CreateViews, DropViews)

## Usage

```csharp
// Create a persistent view (uses IF NOT EXISTS)
connector.CreateView(typeof(CustomerOrderView));

// Check existence via sqlite_master
bool exists = connector.ViewExists("customer_orders_view");

// Async equivalents
await connector.CreateViewAsync(typeof(CustomerOrderView));
bool exists = await connector.ViewExistsAsync("customer_orders_view");
```

## Dependencies

- Birko.Data.SQL
- Birko.Data.SQL.View
- Birko.Data.SQL.SqLite

## Related Projects

- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) - Base view framework
- [Birko.Data.SQL.SqLite](../Birko.Data.SQL.SqLite/) - SQLite connector

## License

MIT License - Copyright 2026 Frantisek Beren
