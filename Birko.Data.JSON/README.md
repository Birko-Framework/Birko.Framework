# Birko.Data.JSON

JSON file-based storage implementation for the Birko Framework. Good reference for understanding store patterns.

## Features

- File-based data persistence (no database required)
- Sync and async stores with bulk operations
- Thread-safe file access with locking
- Human-readable JSON data files
- Auto-save on every operation

## Installation

```bash
dotnet add package Birko.Data.JSON
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- System.Text.Json

## Usage

```csharp
using Birko.Data.JSON.Stores;

var store = new JsonStore<Customer>("data/customers.json");

// Create
var id = store.Create(new Customer { Name = "John Doe" });

// Read
var customer = new Customer { Id = id };
store.Read(customer);

// Update
customer.Name = "Jane Doe";
store.Update(customer);

// Read all
var customers = store.ReadAll();

// Delete
store.Delete(customer);
```

### Settings

```csharp
var settings = new JsonSettings { FilePath = "data/customers.json" };
store.SetSettings(settings);
```

## API Reference

### Stores

- **JsonStore\<T\>** - Sync JSON file store
- **JsonBulkStore\<T\>** - Bulk operations
- **AsyncJsonStore\<T\>** - Async store
- **AsyncJsonBulkStore\<T\>** - Async bulk store

### Repositories

- **JsonRepository\<T\>** / **JsonBulkRepository\<T\>**
- **AsyncJsonRepository\<T\>** / **AsyncJsonBulkRepository\<T\>**

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.JSON.ViewModel](../Birko.Data.JSON.ViewModel/) - JSON ViewModel repositories

## Filter-Based Bulk Operations

Supports filter-based update and delete via default read-modify-save pattern inherited from AbstractBulkStore (file-based storage has no native filter-based bulk operations).

## Choosing it by configuration (TASK-990)

`p.AddJson()` registers this backend with `StoreFactory.Create` under the name `Json`, so code that takes an
`IStoreFactory` runs on it without naming it. It takes a folder (or `Path=…`); each type is its own file there, named by its full type name. The factory keeps one store per type. See
[Birko.Data.Stores — Choosing the backend by configuration](../Birko.Data.Stores/README.md).

## License

Part of the Birko Framework.
