# Birko.Data.SQL.PostgreSQL

## Overview
PostgreSQL implementation of Birko.Data.SQL stores and repositories.

## Project Location
`C:\Source\Birko.Data.SQL.PostgreSQL\`

## Purpose
- Provides PostgreSQL-specific data store implementations
- PostgreSQL connector management
- Support for PostgreSQL-specific data types

## Components

### Stores
- `PostgreSQLStore<T>` - Synchronous PostgreSQL store
- `PostgreSQLBulkStore<T>` - Bulk operations store
- `AsyncPostgreSQLStore<T>` - Asynchronous PostgreSQL store
- `AsyncPostgreSQLBulkStore<T>` - Async bulk operations store

### Repositories
- `PostgreSQLRepository<T>` - PostgreSQL repository
- `PostgreSQLBulkRepository<T>` - Bulk repository
- `AsyncPostgreSQLRepository<T>` - Async repository
- `AsyncPostgreSQLBulkRepository<T>` - Async bulk repository

### Connector
- `PostgreSQLConnector` - PostgreSQL connection management

## Database Connection

Connection string format:
```
Host=server_address;Port=5432;Database=database_name;Username=user;Password=password;
```

## Implementation

```csharp
using Birko.Data.SQL.PostgreSQL.Stores;
using Npgsql;

public class CustomerStore : PostgreSQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO customers (id, name, email)
            VALUES ($1, $2, $3)";

        cmd.Parameters.AddWithValue(item.Id);
        cmd.Parameters.AddWithValue(item.Name);
        cmd.Parameters.AddWithValue(item.Email);

        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

## Bulk Operations

PostgreSQL uses COPY for efficient bulk operations:

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    using (var writer = Connector.BeginBinaryImport("COPY customers (id, name, email) FROM STDIN BINARY"))
    {
        foreach (var item in items)
        {
            writer.StartRow();
            writer.Write(item.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
            writer.Write(item.Name, NpgsqlTypes.NpgsqlDbType.Text);
            writer.Write(item.Email, NpgsqlTypes.NpgsqlDbType.Text);
        }
        writer.Complete();
    }
}
```

## Data Types

Common PostgreSQL to .NET type mappings:
- `UUID` → `Guid`
- `TEXT/VARCHAR(n)` → `string`
- `INTEGER` → `int`
- `BIGINT` → `long`
- `NUMERIC(p,s)` → `decimal`
- `TIMESTAMP` → `DateTime`
- `TIMESTAMPTZ` → `DateTime` (with timezone)
- `BOOLEAN` → `bool`
- `JSONB` → `string` (or mapped object)
- `ARRAY` → `T[]`

## PostgreSQL Specific Features

### RETURNING Clause
Get inserted/updated values:
```sql
INSERT INTO customers (name, email)
VALUES ($1, $2)
RETURNING id
```

### Arrays
PostgreSQL supports array columns:
```csharp
cmd.Parameters.AddWithValue(new string[] { "tag1", "tag2" });
```

### JSONB
Native JSON support:
```sql
CREATE TABLE products (
    id UUID PRIMARY KEY,
    data JSONB
);
```

## Dependencies
- Birko.Data
- Birko.Data.SQL
- Npgsql (PostgreSQL driver)

## Naming Conventions

PostgreSQL commonly uses lowercase with underscores:
- Table names: `customers`, `orders`
- Column names: `customer_id`, `created_at`

## Important Notes

### Settings Handling
Pass `RemoteSettings` through base class:
```csharp
public override void SetSettings(Settings settings)
{
    base.SetSettings(settings); // Correct - creates connector from settings
}
```

Do NOT create settings inline:
```csharp
// WRONG - PasswordSettings doesn't have UserName/Port
var settings = new PasswordSettings { UserName = "...", Port = 5432 };
```

### Parameters
PostgreSQL uses positional parameters ($1, $2, ...) or named parameters:

```csharp
// Positional (recommended for PostgreSQL)
cmd.CommandText = "SELECT * FROM customers WHERE id = $1";

// Named (also works)
cmd.CommandText = "SELECT * FROM customers WHERE id = @id";
```

## Limitations
- Requires PostgreSQL 9.5 or later
- Some features may require specific versions

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
