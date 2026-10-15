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
using Birko.Data.SQL.Attributes;

[View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId),
      name: "CustomerOrders", connect: ViewConnect.Check)]
public class CustomerOrderView
{
    [ViewField(typeof(CustomerModel), nameof(CustomerModel.Name))]
    public string CustomerName { get; set; } = null!;

    [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
    public int OrderCount { get; set; }
}
```

**The join** is on the two named keys. `connect:` picks its type: `ViewConnect.Check` is `LEFT OUTER JOIN`,
`CheckExisting` and the default `None` are `INNER JOIN`. Before TASK-626 the default was a `CROSS JOIN` whose condition
was not rendered, so a view declared without `connect:` returned the cartesian product of the two tables.

In code (`Join.Create`), a join given conditions and no type is `Inner`; a `Cross` join carrying conditions, and an
`Inner` or `LeftOuter` join with none, are refused with `NotSupportedException` (a cross join has no ON; an inner join
with nothing to join on is not SQL). A `Cross` join with no conditions renders `CROSS JOIN`.

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
- **FunctionField** - Aggregate function fields with `CreateFunctionField()` static helper for typed field creation from function name and source field

## View Query Modes

The framework supports multiple query modes via the `ViewQueryMode` enum:

| Mode | Description |
|------|-------------|
| `OnTheFly` | Generates a SELECT statement at query time without creating a database view object. Default mode. |
| `Persistent` | Queries an existing database VIEW object (must be created first via DDL methods). |
| `Auto` | Uses the persistent view if it exists, otherwise falls back to on-the-fly generation. |

```csharp
// Query a persistent view directly
var results = connector.SelectView<CustomerOrderView>(ViewQueryMode.Persistent);

// Auto mode: uses persistent view if available, on-the-fly otherwise
var results = connector.SelectView<CustomerOrderView>(ViewQueryMode.Auto);
```

## Materialized View Types

The `MaterializedViewType` enum identifies the materialization strategy across providers:

| Type | Provider | Description |
|------|----------|-------------|
| `None` | All | Standard (non-materialized) view |
| `Materialized` | PostgreSQL | PostgreSQL MATERIALIZED VIEW with explicit refresh |
| `Indexed` | SQL Server | Indexed view with SCHEMABINDING and clustered index |

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
