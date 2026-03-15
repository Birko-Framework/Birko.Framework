# Birko.Data.MongoDB

## Overview
MongoDB implementation for the Birko data layer providing document-based storage.

## Project Location
`C:\Source\Birko.Data.MongoDB\`

## Purpose
- Document-based storage
- Flexible schema design
- High performance for read-heavy workloads
- Horizontal scaling support

## Components

### Stores
- `MongoDBStore<T>` - Synchronous MongoDB store
- `MongoDBBulkStore<T>` - Bulk operations store
- `AsyncMongoDBStore<T>` - Asynchronous MongoDB store
- `AsyncMongoDBBulkStore<T>` - Async bulk operations store

### Repositories
- `MongoDBRepository<T>` - MongoDB repository
- `MongoDBBulkRepository<T>` - Bulk repository
- `AsyncMongoDBRepository<T>` - Async repository
- `AsyncMongoDBBulkRepository<T>` - Async bulk repository

## Connection

Connection string format:
```
mongodb://[username:password@]host[:port][/database][?options]
```

Example:
```csharp
var settings = new MongoDBSettings
{
    ConnectionString = "mongodb://localhost:27017",
    DatabaseName = "myapp",
    CollectionName = "entities"
};
```

## Implementation

```csharp
using Birko.Data.MongoDB.Stores;
using MongoDB.Driver;

public class CustomerStore : MongoDBStore<Customer>
{
    public CustomerStore(MongoDBSettings settings) : base(settings)
    {
    }

    public override Guid Create(Customer item)
    {
        var collection = Database.GetCollection<Customer>(Settings.CollectionName);
        collection.InsertOne(item);
        return item.Id;
    }

    public override void Read(Customer item)
    {
        var collection = Database.GetCollection<Customer>(Settings.CollectionName);
        var filter = Builders<Customer>.Filter.Eq(x => x.Id, item.Id);
        var result = collection.Find(filter).FirstOrDefault();

        if (result != null)
        {
            CopyProperties(result, item);
        }
        else
        {
            throw new NotFoundException($"Customer {item.Id} not found");
        }
    }
}
```

## Bulk Operations

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    var collection = Database.GetCollection<Customer>(Settings.CollectionName);
    collection.InsertMany(items);
    return items.Select(item => new KeyValuePair<Customer, Guid>(item, item.Id));
}
```

## Update Operations

```csharp
public override void Update(Customer item)
{
    var collection = Database.GetCollection<Customer>(Settings.CollectionName);
    var filter = Builders<Customer>.Filter.Eq(x => x.Id, item.Id);
    collection.ReplaceOne(filter, item);
}
```

## Querying

```csharp
public IEnumerable<Customer> GetByEmail(string email)
{
    var collection = Database.GetCollection<Customer>(Settings.CollectionName);
    var filter = Builders<Customer>.Filter.Eq(x => x.Email, email);
    return collection.Find(filter).ToEnumerable();
}
```

## Indexes

Create indexes for better query performance:

```csharp
var collection = Database.GetCollection<Customer>(Settings.CollectionName);
var indexKeysDefinition = Builders<Customer>.IndexKeys
    .Ascending(x => x.Email)
    .Descending(x => x.CreatedAt);

collection.Indexes.CreateOne(new CreateIndexModel<Customer>(indexKeysDefinition));
```

## Dependencies
- Birko.Data.Core, Birko.Data.Stores
- MongoDB.Driver (official MongoDB .NET driver)
- MongoDB Server 4.0 or later

## Data Types

Common .NET to BSON type mappings:
- `Guid` → `BinData(3)` (UUID)
- `string` → `String`
- `int` → `Int32`
- `long` → `Int64`
- `double` → `Double`
- `decimal` → `Decimal128` (MongoDB 3.4+)
- `bool` → `Boolean`
- `DateTime` → `DateTime`
- `byte[]` → `BinData`
- `List<T>` → `Array`
- `Dictionary<K,V>` → `Object`

## Features

### Flexible Schema
Each document can have different fields:

```csharp
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public BsonDocument ExtraData { get; set; } // Dynamic fields
}
```

### Embedded Documents
```csharp
public class Order
{
    public Guid Id { get; set; }
    public List<OrderLine> Lines { get; set; } // Embedded
}

public class OrderLine
{
    public string Product { get; set; }
    public int Quantity { get; set; }
}
```

### Transactions
MongoDB 4.0+ supports multi-document transactions:

```csharp
using (var session = Client.StartSession())
{
    session.StartTransaction();
    try
    {
        // Operations
        session.CommitTransaction();
    }
    catch
    {
        session.AbortTransaction();
    }
}
```

## Best Practices

### Index Strategy
- Index fields used in queries
- Create compound indexes for common query patterns
- Use unique indexes for unique fields

### Document Design
- Embed related data for read performance
- Use references for many-to-many relationships
- Keep documents under 16MB (BSON limit)

### Connection Management
- Use a single MongoClient instance
- Connection pooling is automatic
- Configure pool size for your workload

## Use Cases
- Content management systems
- Product catalogs
- User profiles
- Real-time analytics
- Time-series data (with appropriate schema)

## Limitations
- No foreign key constraints
- Document size limit (16MB)
- Memory-intensive for large result sets
- Limited transaction support (4.0+)

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
