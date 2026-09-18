# Birko.Data.SQL.MySQL.View

MySQL-specific view DDL support for the Birko.Data.SQL.View framework.

## Features

- **CREATE OR REPLACE VIEW** (native MySQL syntax, uses base default)
- **ViewExists** check via `information_schema.VIEWS` with `TABLE_SCHEMA = DATABASE()` filtering
- Inherits all base view operations from Birko.Data.SQL.View (CreateView, DropView, RecreateView, CreateViewIfNotExists, CreateViews, DropViews)

## Usage

```csharp
// Create a persistent view
connector.CreateView(typeof(CustomerOrderView));

// Check existence via information_schema (scoped to current database)
bool exists = connector.ViewExists("customer_orders_view");

// Async equivalents
await connector.CreateViewAsync(typeof(CustomerOrderView));
bool exists = await connector.ViewExistsAsync("customer_orders_view");
```

## Dependencies

- Birko.Data.SQL
- Birko.Data.SQL.View
- Birko.Data.SQL.MySQL

## Related Projects

- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) - Base view framework
- [Birko.Data.SQL.MySQL](../Birko.Data.SQL.MySQL/) - MySQL connector

## License

MIT License - Copyright 2026 Frantisek Beren
