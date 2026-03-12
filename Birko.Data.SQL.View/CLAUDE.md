# Birko.Data.SQL.View

## Overview
SQL view generation framework for creating database views from entity attributes.

## Project Location
`C:\Source\Birko.Data.SQL.View\`

## Purpose
- Generate database views from C# attributes
- Automate view creation
- Support for complex view definitions
- Cross-database compatibility

## Components

### Attributes
- `ViewAttribute` - Marks entity for view generation
- `ViewColumnAttribute` - Defines view column
- `ViewJoinAttribute` - Defines view joins
- `ViewFilterAttribute` - Defines view filters

### SQL
- `ViewGenerator<T>` - View generator
- `ViewBuilder` - View query builder
- `ViewDefinition` - View metadata

### Connectors
- Database-specific view creation

## Creating a View

```csharp
using Birko.Data.SQL.View.Attributes;

[View("customer_orders_view")]
public class CustomerOrderView
{
    [ViewColumn("customer_id", "Id")]
    public Guid CustomerId { get; set; }

    [ViewColumn("customer_name", "Name")]
    public string CustomerName { get; set; }

    [ViewColumn("order_count")]
    public int OrderCount { get; set; }

    [ViewColumn("total_spent")]
    public decimal TotalSpent { get; set; }
}
```

## Generating the View

```csharp
using Birko.Data.SQL.View;

var generator = new ViewGenerator<CustomerOrderView>();
var sql = generator.GenerateCreateView(connection);

// Executes:
// CREATE VIEW customer_orders_view AS
// SELECT
//     c.Id AS customer_id,
//     c.Name AS customer_name,
//     COUNT(o.Id) AS order_count,
//     SUM(o.Total) AS total_spent
// FROM Customers c
// LEFT JOIN Orders o ON o.CustomerId = c.Id
// GROUP BY c.Id, c.Name
```

## View with Joins

```csharp
[View("product_category_view")]
[ViewJoin("Categories", "CategoryId", "Id")]
public class ProductCategoryView
{
    [ViewColumn("Id")]
    public Guid ProductId { get; set; }

    [ViewColumn("Name")]
    public string ProductName { get; set; }

    [ViewColumn("CategoryName", "Categories.Name")]
    public string CategoryName { get; set; }
}
```

## View with Filters

```csharp
[View("active_users_view")]
[ViewFilter("IsActive = true")]
[ViewFilter("DeletedAt IS NULL")]
public class ActiveUsersView
{
    [ViewColumn("Id")]
    public Guid Id { get; set; }

    [ViewColumn("Email")]
    public string Email { get; set; }
}
```

## Materialized Views

For PostgreSQL:

```csharp
[View("user_stats", Materialized = true, RefreshInterval = "1 hour")]
public class UserStatsView
{
    [ViewColumn("UserId")]
    public Guid UserId { get; set; }

    [ViewColumn("LoginCount")]
    public int LoginCount { get; set; }
}
```

## Dependencies
- Birko.Data
- Birko.Data.SQL

## Supported Databases
- PostgreSQL (including materialized views)
- SQL Server (indexed views)
- MySQL
- SQLite

## Best Practices

1. **View naming** - Use descriptive names with `_view` suffix
2. **Column aliases** - Always provide readable column names
3. **Indexes** - Create indexes on views for performance
4. **Refresh strategy** - For materialized views, set appropriate refresh intervals
5. **Dependencies** - Be aware of underlying table changes

## Use Cases
- Reporting
- Data aggregation
- Security (column-level)
- Simplified queries
- Performance optimization
- Data isolation

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
