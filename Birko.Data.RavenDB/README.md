# Birko.Data.RavenDB

RavenDB document-based storage implementation for the Birko Framework with ACID transactions and built-in caching.

## Features

- Document-based CRUD with ACID transactions
- Built-in caching and optimistic concurrency
- Full-text search via LINQ
- Include/Load pattern for reducing round trips
- Document patching without loading

## Installation

```bash
dotnet add package Birko.Data.RavenDB
```

## Dependencies

- Birko.Data
- RavenDB.Client

## Usage

```csharp
using Birko.Data.RavenDB.Stores;

var settings = new RavenDBSettings
{
    Url = "http://localhost:8080",
    DatabaseName = "MyApp"
};

var store = new RavenDBStore<Customer>(settings);
var id = store.Create(customer);

// Query with LINQ
using (var session = Store.OpenSession())
{
    var customers = session.Query<Customer>()
        .Where(c => c.Email == email)
        .ToList();
}
```

## API Reference

### Stores

- **RavenDBStore\<T\>** - Sync store
- **RavenDBBulkStore\<T\>** - Bulk operations
- **AsyncRavenDBStore\<T\>** - Async store
- **AsyncRavenDBBulkStore\<T\>** - Async bulk store

### Repositories

- **RavenDBRepository\<T\>** / **RavenDBBulkRepository\<T\>**
- **AsyncRavenDBRepository\<T\>** / **AsyncRavenDBBulkRepository\<T\>**

## Related Projects

- [Birko.Data](../Birko.Data/) - Core interfaces
- [Birko.Data.RavenDB.ViewModel](../Birko.Data.RavenDB.ViewModel/) - ViewModel repositories

## License

Part of the Birko Framework.
