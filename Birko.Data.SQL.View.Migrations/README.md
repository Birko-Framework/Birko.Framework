# Birko.Data.SQL.View.Migrations

Integration between SQL View definitions and the Migration framework.

## Features

- **ViewSqlGenerator** generates static DDL (CREATE VIEW / DROP VIEW) from view attributes for use in migrations
- **ViewMigrationExtensions** provides extension methods on `SqlMigration` for creating and dropping views within migration steps

## Dependencies

- Birko.Data.SQL.View
- Birko.Data.Migrations.SQL

## Usage

```csharp
using Birko.Data.SQL.View.Migrations;

public class AddCustomerOrdersView : SqlMigration
{
    public override void Up()
    {
        // Create view from attributed class
        this.CreateView<CustomerOrderView>();
    }

    public override void Down()
    {
        // Drop the view
        this.DropView("customer_orders_view");
    }
}
```

### Provider-Specific Quoting

The `ViewSqlGenerator` accepts a `quoteChar` parameter for provider-specific identifier quoting:

```csharp
// SQL Server: [column_name]
var sql = ViewSqlGenerator.GenerateCreateViewSql<CustomerOrderView>(quoteChar: ("\"", "\""));

// MySQL: `column_name`
var sql = ViewSqlGenerator.GenerateCreateViewSql<CustomerOrderView>(quoteChar: ("`", "`"));
```

## API Reference

- **ViewSqlGenerator** - Static methods for generating CREATE/DROP VIEW DDL from view attributes
  - `GenerateCreateViewSql<T>(quoteChar)` — generates CREATE VIEW statement
  - `GenerateDropViewSql<T>()` — generates DROP VIEW IF EXISTS statement
- **ViewMigrationExtensions** - Extension methods on `SqlMigration`
  - `CreateView<T>()` — adds CREATE VIEW step to the migration
  - `DropView(viewName)` — adds DROP VIEW step to the migration

## Related Projects

- [Birko.Data.SQL.View](../Birko.Data.SQL.View/) - Base view framework
- [Birko.Data.Migrations.SQL](../Birko.Data.Migrations.SQL/) - SQL migration framework

## License

Part of the Birko Framework.
