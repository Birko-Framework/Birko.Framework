# Birko.Data.Migrations.SQL

## Overview
SQL-specific database migrations for the Birko data layer.

## Project Location
`C:\Source\Birko.Data.Migrations.SQL\`

## Purpose
- Schema migrations for SQL databases
- Support for multiple SQL dialects
- Transaction support
- Rollback capabilities

## Components

### Abstract Classes
- `SQLMigration<Connection>` - Base SQL migration class
- `SQLMigrationRunner<Connection>` - SQL migration runner

### Settings
- `SQLMigrationSettings` - SQL migration configuration

## Creating a SQL Migration

```csharp
using Birko.Data.Migrations.SQL;
using System.Data.SqlClient;

public class Migration_2024_01_01_CreateUsersTable : SQLMigration<SqlConnection>
{
    public override string Name => "CreateUsersTable";

    public override string Version => "2024.01.01";

    public override void Up(SqlConnection connection, SqlTransaction transaction)
    {
        var sql = @"
            CREATE TABLE Users (
                Id UNIQUEIDENTIFIER PRIMARY KEY,
                Email NVARCHAR(256) NOT NULL,
                Name NVARCHAR(256) NOT NULL,
                CreatedAt DATETIME2 DEFAULT GETDATE()
            )";

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    public override void Down(SqlConnection connection, SqlTransaction transaction)
    {
        var sql = "DROP TABLE Users";

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
```

## Database-Specific Migrations

Different databases use different connection types:

### SQL Server
```csharp
public class MyMigration : SQLMigration<SqlConnection>
{
    // Use System.Data.SqlClient
}
```

### PostgreSQL
```csharp
public class MyMigration : SQLMigration<NpgsqlConnection>
{
    // Use Npgsql
}
```

### MySQL
```csharp
public class MyMigration : SQLMigration<MySqlConnection>
{
    // Use MySql.Data.MySqlClient
}
```

### SQLite
```csharp
public class MyMigration : SQLMigration<SqliteConnection>
{
    // Use Microsoft.Data.Sqlite
}
```

## Running Migrations

```csharp
var settings = new SQLMigrationSettings
{
    ConnectionString = "your-connection-string",
    DatabaseName = "your-database"
};

var runner = new SQLMigrationRunner<SqlConnection>(settings);
runner.RunMigrations();
```

## Features

### Transaction Support
All migrations run within transactions for atomicity.

### Migration History
Tracks executed migrations:
```sql
CREATE TABLE __Migrations (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Version NVARCHAR(20) NOT NULL,
    Name NVARCHAR(256) NOT NULL,
    AppliedAt DATETIME2 NOT NULL
);
```

### Rollback
```csharp
runner.RollbackToVersion("2024.01.01");
```

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Migrations
- Birko.Data.SQL

## Supported Databases
- Microsoft SQL Server
- PostgreSQL
- MySQL
- SQLite
- TimescaleDB (via PostgreSQL)

## Best Practices

### Transactions
Always use the provided transaction:
```csharp
cmd.Transaction = transaction; // Important!
```

### Error Handling
Let exceptions propagate - they will rollback the transaction.

### Idempotency
Check for existing objects:
```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (...)
END
```

### Naming
Use descriptive names with year/month/day prefix:
- `Migration_2024_01_01_CreateUsersTable`
- `Migration_2024_01_02_AddEmailIndex`
- `Migration_2024_01_03_DropOldColumn`

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
