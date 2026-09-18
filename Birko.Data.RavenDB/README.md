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

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- RavenDB.Client

## Usage

```csharp
using Birko.Data.RavenDB.Stores;

var settings = new Birko.Data.RavenDB.Stores.Settings
{
    Location = "http://localhost:8080",
    Name = "MyApp"
};

var store = new RavenDBStore<Customer>();
store.SetSettings(settings);
await store.InitAsync();
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

### Index Management

```csharp
using Birko.Data.RavenDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;

var indexManager = new RavenDBIndexManager(documentStore);

// Create index via uniform IIndexManager interface
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "Orders/ByCustomer",
    Fields = new[] { IndexField.Ascending("CustomerId") },
    Properties = new Dictionary<string, object>
    {
        ["Map"] = "from o in docs.Orders select new { o.CustomerId, o.Total }"
    }
});

// List all indexes, check stale
var indexes = await indexManager.ListAsync();
var stale = await indexManager.GetStaleIndexesAsync();

// RavenDB-specific: reset, enable/disable, priority
await indexManager.ResetAsync("Orders/ByCustomer");
await indexManager.DisableAsync("Orders/ByCustomer");
await indexManager.EnableAsync("Orders/ByCustomer");
await indexManager.SetPriorityAsync("Orders/ByCustomer", IndexPriority.High);

// Deploy from AbstractIndexCreationTask
await indexManager.CreateFromTaskAsync<Orders_ByCustomer>();
```

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.RavenDB.ViewModel](../Birko.Data.RavenDB.ViewModel/) - ViewModel repositories

## Filter-Based Bulk Operations

Supports filter-based update and delete via default read-modify-save pattern inherited from AbstractBulkStore. Native RavenDB `PatchByQueryOperation` support may be added in a future release.

## License

Part of the Birko Framework.
