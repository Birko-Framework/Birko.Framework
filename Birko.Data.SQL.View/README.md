# Birko.Data.SQL.View

SQL view generation framework for creating database views from C# entity attributes.

## Features

- Attribute-based view definition
- Automatic view SQL generation
- Join and filter support
- Materialized view support (PostgreSQL)
- Cross-database compatibility (SQL Server, PostgreSQL, MySQL, SQLite)

## Installation

```bash
dotnet add package Birko.Data.SQL.View
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces)
- Birko.Data.SQL

## Usage

### Define a View

```csharp
using Birko.Data.SQL.View.Attributes;

[View("customer_orders_view")]
[ViewJoin("Orders", "Id", "CustomerId")]
public class CustomerOrderView
{
    [ViewColumn("customer_id", "Id")]
    public Guid CustomerId { get; set; }

    [ViewColumn("customer_name", "Name")]
    public string CustomerName { get; set; }

    [ViewColumn("order_count")]
    public int OrderCount { get; set; }
}
```

### Generate the View

```csharp
var generator = new ViewGenerator<CustomerOrderView>();
var sql = generator.GenerateCreateView(connection);
```

### Filtered View

```csharp
[View("active_users_view")]
[ViewFilter("IsActive = true")]
[ViewFilter("DeletedAt IS NULL")]
public class ActiveUsersView { /* ... */ }
```

### Materialized View (PostgreSQL)

```csharp
[View("user_stats", Materialized = true, RefreshInterval = "1 hour")]
public class UserStatsView { /* ... */ }
```

## API Reference

### Attributes

- **ViewAttribute** - Marks entity for view generation (name, materialized, refresh)
- **ViewColumnAttribute** - Defines view column mapping
- **ViewJoinAttribute** - Defines table joins
- **ViewFilterAttribute** - Defines WHERE filters

### Classes

- **ViewGenerator\<T\>** - Generates CREATE VIEW SQL
- **ViewBuilder** - View query builder
- **ViewDefinition** - View metadata

## Persistent View DDL

In addition to on-the-fly SELECT generation, the framework supports creating and managing actual database VIEW objects.

### Create / Drop Views

```csharp
// Sync
connector.CreateView(typeof(CustomerOrderView));
connector.CreateViewIfNotExists(typeof(CustomerOrderView));
connector.DropView("customer_orders_view");
connector.RecreateView(typeof(CustomerOrderView));

// Async
await connector.CreateViewAsync(typeof(CustomerOrderView));
await connector.CreateViewIfNotExistsAsync(typeof(CustomerOrderView));
await connector.DropViewAsync("customer_orders_view");
await connector.RecreateViewAsync(typeof(CustomerOrderView));
```

### Check View Existence

```csharp
bool exists = connector.ViewExists("customer_orders_view");
bool exists = await connector.ViewExistsAsync("customer_orders_view");
```

### Batch Operations

```csharp
connector.CreateViews(new[] { typeof(View1), typeof(View2) });
connector.DropViews(new[] { "view1", "view2" });
```

### Database-Specific Behavior

Each SQL provider has a separate view project with DDL overrides:

| Provider | CREATE syntax | ViewExists catalog |
|----------|--------------|-------------------|
| [MSSql](../Birko.Data.SQL.MSSql.View/) | `CREATE OR ALTER VIEW` | `sys.views` |
| [PostgreSQL](../Birko.Data.SQL.PostgreSQL.View/) | `CREATE OR REPLACE VIEW` | `information_schema.views` |
| [MySQL](../Birko.Data.SQL.MySQL.View/) | `CREATE OR REPLACE VIEW` | `information_schema.VIEWS` |
| [SQLite](../Birko.Data.SQL.SqLite.View/) | `CREATE VIEW IF NOT EXISTS` | `sqlite_master` |

PostgreSQL also supports materialized views via `CreateMaterializedView`, `RefreshMaterializedView`, and `DropMaterializedView`.

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes
- [Birko.Data.SQL.MSSql.View](../Birko.Data.SQL.MSSql.View/) - SQL Server view DDL
- [Birko.Data.SQL.PostgreSQL.View](../Birko.Data.SQL.PostgreSQL.View/) - PostgreSQL view DDL
- [Birko.Data.SQL.MySQL.View](../Birko.Data.SQL.MySQL.View/) - MySQL view DDL
- [Birko.Data.SQL.SqLite.View](../Birko.Data.SQL.SqLite.View/) - SQLite view DDL

## License

Part of the Birko Framework.
