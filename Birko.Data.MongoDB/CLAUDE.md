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

### Serialization
- `MongoSerialization.EnsureRegistered()` — the framework's **one** registration of driver
  serialization, called from the `MongoDBClient` constructor (the single point both stores'
  `SetSettings` pass through), idempotent and thread-safe.
- It registers two things: **`AbstractModel.Guid` as the document `_id`**, represented as a BSON
  string, and a default `GuidSerializer` with `GuidRepresentation.Standard` for every other `Guid`
  member.
- **One identity per document.** TASK-214 first shipped the opposite — `_id` left to the driver as an
  auto-generated ObjectId, the Guid stored beside it — which needed an `IgnoreExtraElements` on the
  base class map just to stop the unwanted `_id` breaking every read. That made every Birko entity a
  silent-drop reader with no per-model opt-out, and it contradicted `Birko.Data.MongoDB.Views`, whose
  translator maps the `Guid` property to `_id`: a view projecting the canonical id threw, and a view
  filtering on it returned zero rows silently (TASK-219). Do not reintroduce a second id.
- **Do not add a member to a model that shadows an `AbstractModel` one.** `BsonClassMap` maps
  *declared* members per class, so a re-declared `Guid` claims the same element name twice and the
  class map refuses to freeze — every derived model becomes unserializable, at first write, not at
  compile time. This is what `MongoDBModel` used to do (TASK-214).
- Both registrations are `TryRegister*` and run after start-up, so a **consumer that configures its
  own** Guid serializer or `AbstractModel` class map first keeps it. Deliberate: a module
  initializer would run earlier and silently override the consumer.

### Change Streams
- Real-time data change notifications via MongoDB Change Streams
- Supports filtering by operation type (insert, update, delete, replace)
- Uses `WatchAsync` on collections for async enumeration of changes
- Requires MongoDB replica set or sharded cluster

### Aggregation Pipeline
- `Aggregate()` fluent pipeline builder for complex data transformations
- Supports `Match`, `Group`, `Sort`, `Limit`, `Project`, `Unwind`, `Lookup` stages
- Type-safe projections and grouping expressions
- Executes server-side for efficient data processing

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
- `AbstractModel.Guid` (the canonical id) → the document `_id`, as a `String` — set by
  `MongoSerialization`, see § Components. There is no separate `Guid` element
- any other `Guid` → `BinData(4)` (UUID **standard**, i.e. `GuidRepresentation.Standard`) — also set
  by `MongoSerialization`. Driver 3.x has no usable default here: it ships `Unspecified`, which
  throws rather than choosing, so nothing serializes until the registration runs
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
