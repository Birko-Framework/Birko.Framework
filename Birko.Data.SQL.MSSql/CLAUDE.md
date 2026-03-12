# Birko.Data.SQL.MSSql

## Overview
Microsoft SQL Server implementation of Birko.Data.SQL stores and repositories.

## Project Location
`C:\Source\Birko.Data.SQL.MSSql\`

## Purpose
- Provides SQL Server-specific data store implementations
- Implements bulk operations using SqlBulkCopy
- MSSql connector management

## Components

### Stores
- `MSSqlStore<T>` - Synchronous SQL Server store
- `MSSqlBulkStore<T>` - Bulk operations store
- `AsyncMSSqlStore<T>` - Asynchronous SQL Server store
- `AsyncMSSqlBulkStore<T>` - Async bulk operations store

### Repositories
- `MSSqlRepository<T>` - SQL Server repository
- `MSSqlBulkRepository<T>` - Bulk repository
- `AsyncMSSqlRepository<T>` - Async repository
- `AsyncMSSqlBulkRepository<T>` - Async bulk repository

### Connector
- `MSSqlConnector` - SQL Server connection management

## Database Connection

Connection string format:
```
Server=server_address;Database=database_name;User Id=user;Password=password;
```

Or using integrated security:
```
Server=server_address;Database=database_name;Integrated Security=True;
```

## Implementation

```csharp
using Birko.Data.SQL.MSSql.Stores;
using System.Data.SqlClient;

public class CustomerStore : MSSqlStore<Customer>
{
    public override Guid Create(Customer item)
    {
        var cmd = Connector.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Customers (Id, Name, Email)
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

Uses `SqlBulkCopy` for efficient bulk inserts:

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    using (var bulkCopy = new SqlBulkCopy(Connector))
    {
        bulkCopy.DestinationTableName = "Customers";
        // Configure column mappings
        // Execute bulk copy
    }
}
```

## Data Types

Common SQL Server to .NET type mappings:
- `UNIQUEIDENTIFIER` → `Guid`
- `NVARCHAR(n)` → `string`
- `INT` → `int`
- `BIGINT` → `long`
- `DECIMAL(p,s)` → `decimal`
- `DATETIME2` → `DateTime`
- `BIT` → `bool`

## Dependencies
- Birko.Data
- Birko.Data.SQL
- System.Data.SqlClient (or Microsoft.Data.SqlClient)

## SQL Server Specific Features

### Identity Columns
For tables with identity columns:
```csharp
cmd.CommandText = "INSERT INTO Customers (Name) VALUES (@Name); SELECT SCOPE_IDENTITY();";
```

### Output Clause
Use OUTPUT clause for inserted values:
```sql
INSERT INTO Customers (Name)
OUTPUT INSERTED.Id
VALUES (@Name)
```

### Transactions
```csharp
using (var transaction = Connector.BeginTransaction())
{
    try
    {
        // Operations
        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

## Limitations
- Requires SQL Server 2012 or later
- Some features may require specific SQL Server editions

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
