# Birko.Data.CosmosDB

Azure Cosmos DB (NoSQL API) document-based storage implementation for the Birko Framework with transactional batch support and LINQ queries.

## Features

- Document-based CRUD with transactional batches (partition-key scoped)
- LINQ-based queries via Microsoft.Azure.Cosmos SDK v3
- Bulk execution for high-throughput operations
- Index management (included paths, composite indexes, spatial indexes)
- Unit of Work pattern via `TransactionalBatch`

## Installation

Add the shared project reference to your `.csproj`:

```xml
<Import Project="..\Birko.Data.CosmosDB\Birko.Data.CosmosDB.projitems" Label="Shared" />
```

## Connection

### Via Typed Settings

```csharp
var settings = new Birko.Data.CosmosDB.Stores.Settings
{
    Location = "AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=...",
    Name = "MyDatabase",       // database name
    UserName = "MyContainer"   // container name (optional, defaults to type name)
};

var store = new AsyncCosmosDBStore<Customer>();
store.SetSettings(settings);
await store.InitAsync();
```

### Via Connection String

```csharp
var store = new AsyncCosmosDBStore<Customer>(
    connectionString: "AccountEndpoint=https://...;AccountKey=...",
    databaseName: "MyDatabase",
    containerName: "Customers"
);
await store.InitAsync();
```

### Via Existing Container

```csharp
var cosmosClient = new CosmosClient("AccountEndpoint=...");
var container = cosmosClient.GetContainer("MyDatabase", "Customers");
var store = new AsyncCosmosDBStore<Customer>(container);
```

## Usage

### Async CRUD

```csharp
var store = new AsyncCosmosDBStore<Customer>(connectionString, "MyDb");
await store.InitAsync();

// Create
var id = await store.CreateAsync(new Customer { Name = "John" });

// Read by ID
var customer = await store.ReadAsync(id);

// Query with LINQ
var customer = await store.ReadAsync(c => c.Name == "John");

// Update
customer.Name = "Jane";
await store.UpdateAsync(customer);

// Upsert (create or update)
await store.SaveAsync(customer);

// Delete
await store.DeleteAsync(customer);

// Count
var count = await store.CountAsync(c => c.IsActive);
```

### Bulk Operations

```csharp
// Bulk read with filter, ordering, paging
var customers = await store.ReadAsync(
    filter: c => c.IsActive,
    orderBy: new OrderBy<Customer>("Name"),
    limit: 50,
    offset: 0
);

// Bulk create (uses parallel execution)
await store.CreateAsync(customerList);

// Bulk update
await store.UpdateAsync(customerList);

// Bulk delete
await store.DeleteAsync(customerList);
```

### Repositories

```csharp
// ViewModel repository
var repo = new AsyncCosmosDBRepository<CustomerViewModel, Customer>(
    connectionString, "MyDb", "Customers"
);

// Model repository
var modelRepo = new AsyncCosmosDBModelRepository<Customer>(
    connectionString, "MyDb", "Customers"
);
```

### Transactional Batch (Unit of Work)

Cosmos DB transactional batches are scoped to a single partition key:

```csharp
await using var uow = CosmosDbUnitOfWork.FromStore(store, new PartitionKey("partition1"));
await uow.BeginAsync();

store.SetTransactionContext(uow.Context);

// All operations in same partition — atomic
await store.CreateAsync(item1);
await store.CreateAsync(item2);

await uow.CommitAsync();
store.SetTransactionContext(null);
```

### Index Management

```csharp
var indexManager = new CosmosDBIndexManager(database);

// List indexes
var indexes = await indexManager.ListAsync("MyContainer");

// Add an included path, under a name
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "IX_Name",
    Fields = new[] { IndexField.Ascending("Name") }
}, scope: "MyContainer");

// Add a composite index
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "IX_Name_Date",
    Fields = new[] { IndexField.Ascending("Name"), IndexField.Descending("CreatedDate") }
}, scope: "MyContainer");

// Both answer by the name they were given
await indexManager.ExistsAsync("IX_Name_Date", "MyContainer");   // true
await indexManager.DropAsync("IX_Name_Date", "MyContainer");     // removes exactly that composite

// Add spatial index
await indexManager.AddSpatialIndexAsync("MyContainer", "/location/*", SpatialType.Point);

// Replace entire indexing policy
await indexManager.SetIndexingPolicyAsync("MyContainer", newPolicy);
```

A Cosmos indexing policy has no index names, so the manager keeps them: one document per indexed container in the
`__birko_indexes` container of the same database, mapping each name to its included path or composite path list. The
policy stays the truth (a registered index exists while its paths are in the policy), entity reads never see the
registry, and the listing reports unregistered policy entries by path. `Unique` is refused: a unique key holds within a
logical partition and the stores partition on `/id`, so it would reject nothing (measured). `Sparse`, `ExpireAfter`,
non-standard field types, a single descending field and `Properties` are refused too (TASK-628).

## Partition Key

The default partition key path is `/id`. Change it via typed settings:

```csharp
var settings = new Birko.Data.CosmosDB.Stores.Settings
{
    Location = "AccountEndpoint=https://...;AccountKey=...",
    Name = "MyDatabase",
    RequestTimeout = TimeSpan.FromSeconds(60)
    // PartitionKeyPath stays "/id": the stores address documents by Guid, and any other path is refused.
};
var store = new AsyncCosmosDBStore<Customer>();
store.SetSettings(settings);
await store.InitAsync();
```

## Dependencies

- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Patterns
- Microsoft.Azure.Cosmos >= 3.46.1

## Filter-Based Bulk Operations

Supports filter-based update and delete via default read-modify-save pattern inherited from AbstractBulkStore. Cosmos DB has no server-side filter-based bulk operations; `PatchItemAsync` per item could be explored in a future optimization.
