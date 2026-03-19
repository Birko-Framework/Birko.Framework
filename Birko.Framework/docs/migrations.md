# Migration Guide

## Overview

Birko.Data.Migrations provides a framework for managing database schema changes across different storage backends.

## Supported Providers

- **SQL** (`Birko.Data.Migrations.SQL`) - Microsoft SQL Server, PostgreSQL, MySQL, SQLite
- **ElasticSearch** (`Birko.Data.Migrations.ElasticSearch`) - Elasticsearch indices and mappings
- **MongoDB** (`Birko.Data.Migrations.MongoDB`) - MongoDB collections and indexes
- **RavenDB** (`Birko.Data.Migrations.RavenDB`) - RavenDB documents and indexes
- **InfluxDB** (`Birko.Data.Migrations.InfluxDB`) - InfluxDB buckets and measurements
- **TimescaleDB** (`Birko.Data.Migrations.TimescaleDB`) - TimescaleDB hypertables

## Base Migration

All migrations extend `AbstractMigration` from `Birko.Data.Migrations`:

```csharp
using Birko.Data.Migrations;

public abstract class AbstractMigration : IMigration
{
    public abstract long Version { get; }          // Numeric version for ordering
    public abstract string Name { get; }           // Human-readable name
    public virtual string Description => Name;     // Optional description
    public DateTime CreatedAt { get; }             // Set at construction time

    public abstract void Up();                     // Apply migration
    public virtual void Down() { }                 // Rollback (optional override)
}
```

**Key difference from docs previously:** Version is `long` (not string), and the base `Up()`/`Down()` are parameterless.

## SQL Migrations

SQL migrations extend `SqlMigration` from `Birko.Data.Migrations.SQL`:

```csharp
using Birko.Data.Migrations.SQL;

public class CreateUsersTable : SqlMigration
{
    public override long Version => 20260101_001;
    public override string Name => "CreateUsersTable";

    // Option 1: Override SQL strings directly
    protected override string UpSql => @"
        CREATE TABLE Users (
            Id UNIQUEIDENTIFIER PRIMARY KEY,
            Email NVARCHAR(256) NOT NULL,
            Name NVARCHAR(256) NOT NULL,
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )";

    protected override string DownSql => "DROP TABLE Users";
}
```

For more complex migrations, override `ExecuteSql`:

```csharp
public class AddUserIndexes : SqlMigration
{
    public override long Version => 20260101_002;
    public override string Name => "AddUserIndexes";

    protected override void ExecuteSql(DbConnection connection, DbTransaction? transaction,
                                        MigrationDirection direction)
    {
        if (direction == MigrationDirection.Up)
        {
            // Check if table exists before modifying
            if (TableExists(connection, "Users"))
            {
                ExecuteScript(connection, transaction,
                    "CREATE INDEX IX_Users_Email ON Users (Email)");

                if (!ColumnExists(connection, "Users", "LastLogin"))
                {
                    ExecuteScript(connection, transaction,
                        "ALTER TABLE Users ADD LastLogin DATETIME2 NULL");
                }
            }
        }
        else
        {
            ExecuteScript(connection, transaction,
                "DROP INDEX IF EXISTS IX_Users_Email ON Users");
        }
    }
}
```

### SqlMigration Helper Methods

`SqlMigration` provides built-in helpers:

| Method | Description |
|--------|-------------|
| `ExecuteScript(connection, transaction, sql)` | Execute a SQL script |
| `TableExists(connection, tableName)` | Check if a table exists |
| `ColumnExists(connection, tableName, columnName)` | Check if a column exists |
| `AddParameter(command, name, value)` | Add a parameter to a DbCommand |

### PostgreSQL Example

```csharp
public class CreateProductsTable : SqlMigration
{
    public override long Version => 20260101_001;
    public override string Name => "CreateProductsTable";

    protected override string UpSql => @"
        CREATE TABLE products (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            name VARCHAR(256) NOT NULL,
            price NUMERIC(10,2) NOT NULL,
            category VARCHAR(128),
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )";

    protected override string DownSql => "DROP TABLE IF EXISTS products";
}
```

## ElasticSearch Migrations

```csharp
using Birko.Data.Migrations.ElasticSearch;
using Nest;

public class CreateProductsIndex : ElasticSearchMigration
{
    public override long Version => 20260101_001;
    public override string Name => "CreateProductsIndex";

    // ElasticSearch migrations receive IElasticClient
    protected override void ExecuteMigration(IElasticClient client, MigrationDirection direction)
    {
        if (direction == MigrationDirection.Up)
        {
            client.Indices.Create("products", i => i
                .Map<Product>(m => m
                    .Properties(p => p
                        .Keyword(t => t.Name(n => n.Id))
                        .Text(t => t.Name(n => n.Name))
                        .Number(t => t.Name(n => n.Price).Type(NumberType.Double))
                    )
                )
            );
        }
        else
        {
            client.Indices.Delete("products");
        }
    }
}
```

## MongoDB Migrations

```csharp
using Birko.Data.Migrations.MongoDB;
using MongoDB.Driver;

public class CreateUsersCollection : MongoDBMigration
{
    public override long Version => 20260101_001;
    public override string Name => "CreateUsersCollection";

    protected override void ExecuteMigration(IMongoDatabase database, MigrationDirection direction)
    {
        if (direction == MigrationDirection.Up)
        {
            database.CreateCollection("users");
            var collection = database.GetCollection<BsonDocument>("users");

            var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("Email");
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys));
        }
        else
        {
            database.DropCollection("users");
        }
    }
}
```

## MigrationDirection Enum

```csharp
public enum MigrationDirection
{
    Up,
    Down
}
```

## Best Practices

1. **Versioning**: Use numeric versions (e.g., `20260301_001`) for ordering. Migrations run in ascending version order.
2. **Reversibility**: Implement `Down()` for rollback support where possible.
3. **Idempotency**: Use `TableExists()`, `ColumnExists()`, `IF EXISTS` guards.
4. **Small steps**: Each migration should make one logical change.
5. **No data loss**: Prefer `ALTER` over `DROP + CREATE` for schema changes.
6. **Testing**: Test both Up and Down paths.
7. **Transactions**: SQL migrations support transactions via `DbTransaction?` parameter.

## See Also

- [Birko.Data.Migrations](https://github.com/birko/Birko.Data.Migrations)
- [Birko.Data.Migrations.SQL](https://github.com/birko/Birko.Data.Migrations.SQL)
- [Birko.Data.Migrations.ElasticSearch](https://github.com/birko/Birko.Data.Migrations.ElasticSearch)
- [Birko.Data.Migrations.MongoDB](https://github.com/birko/Birko.Data.Migrations.MongoDB)
- [Birko.Data.Migrations.RavenDB](https://github.com/birko/Birko.Data.Migrations.RavenDB)
- [Birko.Data.Migrations.InfluxDB](https://github.com/birko/Birko.Data.Migrations.InfluxDB)
- [Birko.Data.Migrations.TimescaleDB](https://github.com/birko/Birko.Data.Migrations.TimescaleDB)
