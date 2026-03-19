# Birko.Data.SQL.MSSql.View

SQL Server-specific view DDL support for the Birko.Data.SQL.View framework.

## Features

- **CREATE OR ALTER VIEW** syntax (requires SQL Server 2016 SP1+)
- **ViewExists** check via `sys.views` catalog
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
