# Birko.Data.JSON

## Overview
JSON file-based storage implementation for Birko.Data.

## Project Location
`C:\Source\Birko.Data.JSON\`

## Purpose
- Simple file-based data persistence
- No database required
- Good for testing, small applications, and configuration

## Components

### Stores
- `JsonStore<T>` - Synchronous JSON file store
- `JsonBulkStore<T>` - Bulk operations JSON store
- `AsyncJsonStore<T>` - Asynchronous JSON file store
- `AsyncJsonBulkStore<T>` - Async bulk operations JSON store

### Repositories
- `JsonRepository<T>` - JSON repository
- `JsonBulkRepository<T>` - Bulk JSON repository
- `AsyncJsonRepository<T>` - Async JSON repository
- `AsyncJsonBulkRepository<T>` - Async bulk JSON repository

## File Structure

Data is stored as JSON array:
```json
[
  {
    "Id": "123e4567-e89b-12d3-a456-426614174000",
    "Name": "Item 1",
    "Created": "2024-01-01T00:00:00Z"
  },
  {
    "Id": "123e4567-e89b-12d3-a456-426614174001",
    "Name": "Item 2",
    "Created": "2024-01-02T00:00:00Z"
  }
]
```

## Implementation

```csharp
using Birko.Data.JSON.Stores;

public class CustomerStore : JsonStore<Customer>
{
    public CustomerStore(string filePath) : base(filePath)
    {
    }

    // Override methods for custom behavior
}
```

## Settings

```csharp
var settings = new JsonSettings
{
    FilePath = "data/customers.json"
};
store.SetSettings(settings);
```

## Bulk Operations

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    var fileData = ReadFromFile();
    var results = new List<KeyValuePair<Customer, Guid>>();

    foreach (var item in items)
    {
        item.Id = NewGuid();
        fileData.Add(item);
        results.Add(new KeyValuePair<Customer, Guid>(item, item.Id));
    }

    WriteToFile(fileData);
    return results;
}
```

## Async Operations

```csharp
public override async Task<Guid> CreateAsync(Customer item)
{
    var fileData = await ReadFromFileAsync();
    item.Id = NewGuid();
    fileData.Add(item);
    await WriteToFileAsync(fileData);
    return item.Id;
}
```

## Features

### Thread Safety
File locking ensures safe concurrent access.

### Auto-save
Changes are written immediately to disk.

### Pretty Print
JSON output can be formatted for readability.

### Backup
Automatic backup before writes (optional).

## Dependencies
- Birko.Data
- System.Text.Json (or Newtonsoft.Json)

## Advantages
- No database setup required
- Human-readable data files
- Easy to backup and version control
- Simple to debug

## Limitations
- Not suitable for large datasets
- No efficient querying (loads entire file)
- No ACID guarantees
- Concurrent access limited

## Use Cases
- Configuration storage
- Small application data
- Testing and development
- Prototyping
- Single-user applications

## Best Practices

### File Location
Store data in a dedicated directory:
```
app/
├── data/
│   ├── customers.json
│   ├── products.json
│   └── orders.json
```

### Error Handling
Handle file I/O errors:
```csharp
try
{
    store.Create(item);
}
catch (IOException ex)
{
    // Handle file access errors
}
```

### Backup
Regular backups of JSON files are essential.

## Example Usage

```csharp
// Create store
var store = new JsonStore<Customer>("data/customers.json");

// Create
var customer = new Customer { Name = "John Doe" };
var id = store.Create(customer);

// Read
customer.Id = id;
store.Read(customer);

// Update
customer.Name = "Jane Doe";
store.Update(customer);

// Read all
var customers = store.ReadAll();

// Delete
store.Delete(customer);
```

## Reference Implementation
This is an excellent reference for understanding store patterns due to its simplicity.

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
