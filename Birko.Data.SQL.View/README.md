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

- Birko.Data
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

## Related Projects

- [Birko.Data.SQL](../Birko.Data.SQL/) - SQL base classes

## License

Part of the Birko Framework.
