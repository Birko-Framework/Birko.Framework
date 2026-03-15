# Birko.Data.MongoDB

MongoDB document-based storage implementation for the Birko Framework.

## Features

- Document-based CRUD operations (sync/async)
- Bulk operations with InsertMany/BulkWrite
- Flexible schema with embedded documents
- Multi-document transactions (MongoDB 4.0+)
- Index management

## Installation

```bash
dotnet add package Birko.Data.MongoDB
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- MongoDB.Driver

## Usage

```csharp
using Birko.Data.MongoDB.Stores;

var settings = new MongoDBSettings
{
    ConnectionString = "mongodb://localhost:27017",
    DatabaseName = "myapp",
    CollectionName = "customers"
};

var store = new MongoDBStore<Customer>(settings);
var id = store.Create(customer);

// Query
var filter = Builders<Customer>.Filter.Eq(x => x.Email, "john@example.com");
var results = collection.Find(filter).ToList();
```

## API Reference

### Stores

- **MongoDBStore\<T\>** - Sync store
- **MongoDBBulkStore\<T\>** - Bulk operations
- **AsyncMongoDBStore\<T\>** - Async store
- **AsyncMongoDBBulkStore\<T\>** - Async bulk store

### Repositories

- **MongoDBRepository\<T\>** / **MongoDBBulkRepository\<T\>**
- **AsyncMongoDBRepository\<T\>** / **AsyncMongoDBBulkRepository\<T\>**

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.MongoDB.ViewModel](../Birko.Data.MongoDB.ViewModel/) - ViewModel repositories

## License

Part of the Birko Framework.
