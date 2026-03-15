# Birko.Data.SQL.MySQL

## Overview
MySQL implementation of Birko.Data.SQL stores and repositories.

## Project Location
`C:\Source\Birko.Data.SQL.MySQL\`

## Purpose
- Provides MySQL-specific data store implementations
- MySQL connector management
- Support for MySQL-specific data types

## Components

### Stores
- `MySQLStore<T>` - Synchronous MySQL store
- `MySQLBulkStore<T>` - Bulk operations store
- `AsyncMySQLStore<T>` - Asynchronous MySQL store
- `AsyncMySQLBulkStore<T>` - Async bulk operations store

### Repositories
- `MySQLRepository<T>` - MySQL repository
- `MySQLBulkRepository<T>` - Bulk repository
- `AsyncMySQLRepository<T>` - Async repository
- `AsyncMySQLBulkRepository<T>` - Async bulk repository

### Connector
- `MySQLConnector` - MySQL connection management

## Database Connection

Connection string format:
```
Server=server_address;Port=3306;Database=database_name;User Id=user;Password=password;
```

## Implementation

```csharp
using Birko.Data.SQL.MySQL.Stores;
using MySql.Data.MySqlClient;

public class CustomerStore : MySQLStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO customers (id, name, email)
            VALUES (@Id, @Name, @Email)";

        cmd.Parameters.AddWithValue("@Id", item.Id);
        cmd.Parameters.AddWithValue("@Name", item.Name);
        cmd.Parameters.AddWithValue("@Email", item.Email);

        cmd.ExecuteNonQuery();
        return item.Id;
    }
}
```

## Bulk Operations

MySQL uses LOAD DATA INFILE for bulk operations:

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    // Bulk insert implementation using MySQL bulk loader
}
```

## Data Types

Common MySQL to .NET type mappings:
- `CHAR(36)` / `BINARY(16)` → `Guid`
- `VARCHAR(n)` / `TEXT` → `string`
- `INT` → `int`
- `BIGINT` → `long`
- `DECIMAL(p,s)` → `decimal`
- `DATETIME` → `DateTime`
- `TIMESTAMP` → `DateTime`
- `TINYINT(1)` → `bool`
- `JSON` → `string` (or mapped object)

## MySQL Specific Features

### AUTO_INCREMENT
For tables with auto-increment:
```sql
INSERT INTO customers (name, email)
VALUES (@Name, @Email);
SELECT LAST_INSERT_ID();
```

### ON DUPLICATE KEY UPDATE
Upsert pattern:
```sql
INSERT INTO customers (id, name, email)
VALUES (@Id, @Name, @Email)
ON DUPLICATE KEY UPDATE name = VALUES(name), email = VALUES(email);
```

### UUID Functions
MySQL UUID functions:
```sql
INSERT INTO customers (id, name)
VALUES (UUID(), @Name);
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores
- Birko.Data.SQL
- MySql.Data (MySQL connector)

## Naming Conventions

MySQL commonly uses lowercase with underscores:
- Table names: `customers`, `orders`
- Column names: `customer_id`, `created_at`

## Important Notes

### Settings Handling
Pass `RemoteSettings` through base class:
```csharp
public override void SetSettings(Settings settings)
{
    base.SetSettings(settings); // Correct
}
```

Do NOT create settings inline:
```csharp
// WRONG - PasswordSettings doesn't have UserName/Port
var settings = new PasswordSettings { UserName = "...", Port = 3306 };
```

### Parameters
MySQL uses named parameters with @ prefix:
```csharp
cmd.Parameters.AddWithValue("@Id", item.Id);
cmd.Parameters.AddWithValue("@Name", item.Name);
```

### Guid Storage
MySQL doesn't have a native UUID type. Options:
- `CHAR(36)` - String representation (more readable)
- `BINARY(16)` - Compact storage (requires conversion)

## Limitations
- Requires MySQL 5.7 or later
- JSON type requires MySQL 5.7.8+
- Some features may vary by MySQL edition

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
