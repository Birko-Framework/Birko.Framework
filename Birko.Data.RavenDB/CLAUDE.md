# Birko.Data.RavenDB

## Overview
RavenDB implementation for the Birko data layer providing document-based storage with advanced features.

## Project Location
`C:\Source\Birko.Data.RavenDB\`

## Purpose
- Document-based storage
- ACID transactions
- Built-in caching
- Full-text search
- Multi-document transactions

## Components

### Stores
- `RavenDBStore<T>` - Synchronous RavenDB store
- `RavenDBBulkStore<T>` - Bulk operations store
- `AsyncRavenDBStore<T>` - Asynchronous RavenDB store
- `AsyncRavenDBBulkStore<T>` - Async bulk operations store

### Repositories
- `RavenDBRepository<T>` - RavenDB repository
- `RavenDBBulkRepository<T>` - Bulk repository
- `AsyncRavenDBRepository<T>` - Async repository
- `AsyncRavenDBBulkRepository<T>` - Async bulk repository

## Connection

### Settings (Birko.Data.RavenDB.Stores.Settings)
Typed settings class extending `RemoteSettings`:
- `RequestTimeout` (default: 30 seconds) — request timeout applied to `DocumentStore.Conventions`
- `CreateDocumentStore()` — builds and initializes `DocumentStore` from settings

Settings mapping from `RemoteSettings`:
- `Location` = RavenDB server URL
- `Name` = database name

### Legacy Settings (still supported)
`SetSettings(ISettings)` accepts `Birko.Configuration.RemoteSettings` and wraps it into a `Settings` instance with defaults.

Example:
```csharp
var settings = new Birko.Data.RavenDB.Stores.Settings
{
    Location = "http://localhost:8080",
    Name = "MyApp",
    RequestTimeout = TimeSpan.FromSeconds(60)
};
var store = new RavenDBStore<Customer>();
store.SetSettings(settings);
```

## Implementation

```csharp
using Birko.Data.RavenDB.Stores;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

public class CustomerStore : RavenDBStore<Customer>
{
    public CustomerStore(RavenDBSettings settings) : base(settings)
    {
    }

    public override Guid Create(Customer item)
    {
        using (var session = Store.OpenSession())
        {
            session.Store(item, item.Id.ToString());
            session.SaveChanges();
            return item.Id;
        }
    }

    public override void Read(Customer item)
    {
        using (var session = Store.OpenSession())
        {
            var loaded = session.Load<Customer>(item.Id.ToString());
            if (loaded != null)
            {
                CopyProperties(loaded, item);
            }
            else
            {
                throw new NotFoundException($"Customer {item.Id} not found");
            }
        }
    }
}
```

## Async Implementation

```csharp
public override async Task<Guid> CreateAsync(Customer item)
{
    using (var session = Store.OpenAsyncSession())
    {
        await session.StoreAsync(item, item.Id.ToString());
        await session.SaveChangesAsync();
        return item.Id;
    }
}
```

## Bulk Operations

```csharp
public override IEnumerable<KeyValuePair<Customer, Guid>> CreateAll(IEnumerable<Customer> items)
{
    using (var session = Store.OpenSession())
    {
        foreach (var item in items)
        {
            session.Store(item, item.Id.ToString());
        }
        session.SaveChanges();
    }
    return items.Select(item => new KeyValuePair<Customer, Guid>(item, item.Id));
}
```

## Querying

### Load by ID
```csharp
using (var session = Store.OpenSession())
{
    var customer = session.Load<Customer>(id.ToString());
}
```

### Query with LINQ
```csharp
using (var session = Store.OpenSession())
{
    var customers = session.Query<Customer>()
        .Where(c => c.Email == email)
        .ToList();
}
```

### Full-Text Search
```csharp
using (var session = Store.OpenSession())
{
    var customers = session.Query<Customer>()
        .Search(c => c.Name, "John")
        .ToList();
}
```

## Index Management

### RavenDBIndexManager
- `ExistsAsync`, `CreateAsync`, `DropAsync`, `ListAsync`, `GetInfoAsync` — IIndexManager implementation
- `CreateFromTaskAsync<TIndex>()` — Deploy a single AbstractIndexCreationTask
- `DeployFromAssemblyAsync(assembly)` — Scan assembly for all AbstractIndexCreationTask types and deploy them
- `DeployFromAssemblyAsync<TMarker>()` — Generic overload using marker type's assembly
- `ResetAsync`, `EnableAsync`, `DisableAsync`, `SetPriorityAsync`, `GetStaleIndexesAsync`
- Map/Reduce support via Properties dict ("Map", "Reduce" keys)

### Map/Reduce Query Helpers
- `QueryMapReduceAsync<TResult>(indexName, filter?, orderBy?, descending?, skip?, take?)` — Query index with LINQ expressions
- `QueryMapReduceFirstAsync<TResult>(indexName, filter?)` — First-or-default from index
- `CountMapReduceAsync<TResult>(indexName, filter?)` — Count results in index

## Indexes

RavenDB uses indexes for queries:

### Auto Indexes
Created automatically for queries.

### Static Indexes
Define for better performance:

```csharp
public class Customers_ByEmail : AbstractIndexCreationTask<Customer>
{
    public Customers_ByEmail()
    {
        Map = customers => from c in customers
                          select new { c.Email };
    }
}
```

## Transactions

RavenDB supports ACID transactions:

```csharp
using (var session = Store.OpenSession())
{
    // Multiple operations
    session.Store(customer1);
    session.Store(customer2);
    session.Delete(customer3);

    // All or nothing
    session.SaveChanges();
}
```

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- RavenDB.Client (official RavenDB .NET client)
- RavenDB Server 5.0 or later

## Data Types

Common .NET to RavenDB mappings:
- `Guid` → `string` (default) or optimized storage
- `string` → `string`
- `int` → `number`
- `long` → `number`
- `double` → `number`
- `decimal` → `number`
- `bool` → `boolean`
- `DateTime` → `date`
- `byte[]` → `binary`
- `List<T>` → `array`
- `Dictionary<K,V>` → `object`

## Features

### Automatic Caching
Built-in caching for frequently accessed documents.

### Included Documents
Include related documents in a single query:

```csharp
var order = session.Include<Order, Customer>(o => o.CustomerId)
    .Load(orderId);
// Customer document is cached, no extra query needed
```

### Patching
Update documents without loading:

```csharp
session.Advanced.Patch<Customer, string>(
    customerId,
    x => x.Name,
    "New Name"
);
```

### Optimistic Concurrency
Automatic conflict detection with ETag:

```csharp
session.Advanced.UseOptimisticConcurrency = true;
```

### Time Series
Built-in time-series data support (RavenDB 5.2+).

## Best Practices

### Session Management
- Use short-lived sessions
- Always dispose sessions
- One session per unit of work

### Load vs Query
- Use `Load()` for known IDs
- Use `Query()` for searching
- Use `Include()` to reduce round trips

### Indexes
- Define indexes for complex queries
- Use auto indexes for simple queries
- Monitor index performance

## Use Cases
- Document-centric applications
- E-commerce platforms
- Content management
- User management
- Any application requiring ACID transactions

## Advantages over MongoDB
- True ACID transactions
- Better querying capabilities
- Built-in caching
- Easier development experience
- Better .NET integration

## Transaction boundary (TASK-240)

`RavenDbUnitOfWork` opens its session with **`TransactionMode.ClusterWide`** -- a compare-exchange backed
cluster transaction, not a single-node one. That is the stronger guarantee and also the more restrictive:
no patching, attachments, counters or time-series inside the boundary, and conflicts surface as concurrency
exceptions at `SaveChanges`. Which mode you get is stated on `Capabilities`, not left to be discovered.

Writes and rollback are sound: a Raven session sends nothing until `SaveChanges`, so a discarded session
leaves no trace.

- **Reads SPLIT, and the capability declaration is deliberately conservative.** Measured against RavenDB
  7.2, not assumed. `ReadAsync(Guid)` -- Load-by-id -- *does* see the session's unsaved writes, since
  TASK-241 aligned the document id with the entity `Guid` and Raven's identity map can therefore find it.
  Every **query-based** read does not: `ReadAsync(filter)`, `Read()`, `ReadFirst` and `Count` are answered
  by the server from indexes, which know nothing about an unsaved session.
  `Capabilities.ReadsSeeUncommittedWrites` stays **`false`**, because a single bool cannot say "id yes,
  query no" and query reads are the common case -- it states the answer a caller is unsafe to get wrong.
  The query half is a RavenDB property, not a defect, and is not fixable from this side.

Pinned by `Birko.Data.RavenDB.Tests.Stores.RavenTransactionBoundaryLiveTests` (5) and
`RavenDocumentIdLiveTests` (11), both gated on `BIRKO_RAVEN_URL`; set `BIRKO_REQUIRE_LIVE` to make a skip
a failure.

## Document ids: the entity `Guid` IS the document id (TASK-241)

`RavenDocumentId.For(store, entityType, guid)` is the **single producer** of a document's id, and every
write, read, update and delete in both stores routes through it. The id is `{collection}/{guid}`.

**What went wrong.** The stores used to have two answers. The write path called `StoreAsync(data)` with no
id, so Raven's default convention generated one from the collection (`Docs/1-A`), while every
read/update/delete addressed `data.Guid.ToString()`. Those never match. Measured against RavenDB 7.2, with
no transaction anywhere in the picture:

- `ReadAsync(Guid)` returned `null` for a document that exists
- `DeleteAsync(entity)` deleted nothing **and reported success**
- `UpdateAsync(entity)` **inserted a duplicate** instead of replacing

The silent no-op delete is the worst of the three -- a caller deleting a record got no exception and no
deletion. Same family as TASK-219, where MongoDB had two answers for what `_id` is.

- **Why not a Raven id convention.** `Conventions.RegisterAsyncIdConvention<T>` would be one registration
  at a funnel, normally the better shape. It cannot be used: conventions **freeze** on
  `DocumentStore.Initialize()` and Raven *throws* on any later change (measured -- "Conventions has frozen
  after 'DocumentStore.Initialize()'"), and both stores accept an externally-supplied, already-initialised
  `IDocumentStore`. A convention-based fix would have had to be skipped for exactly the constructor a
  consumer with its own `DocumentStore` uses -- the silent half-fix this defect is made of.
- **Why the collection prefix**, rather than the bare guid the reads already used. Two entity types
  deliberately sharing one identity -- a `User` and its `UserProfile` keyed by the same `Guid` -- is an
  ordinary modelling pattern, and a bare-guid id would let the second silently overwrite the first: the
  same class of silent data loss. It costs nothing and matches what Raven's own conventions produce, so
  ids stay navigable in the Studio. Pinned by `Two_types_sharing_one_Guid_do_not_overwrite_each_other`,
  which fails if the prefix is dropped.
- **Migration.** Changing an id layout normally costs a migration. Measured before the change: no consumer
  uses RavenDB (Symbio is the only repo that references these stores and is configured for SQLite), so
  there was no stored data to orphan. **That window closes the moment a consumer stores data on Raven** --
  check before assuming it is still free.
- ⚠ **A new write path must supply the id.** Unlike a convention, this cannot enforce itself. Any new
  `Store`/`StoreAsync` call has to pass `IdOf(...)`; the round-trip tests are what would catch a miss.

Mutation-tested: unwiring the id from the nine write sites fails 8 of 66; dropping the collection prefix
fails 2 of 66.

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
