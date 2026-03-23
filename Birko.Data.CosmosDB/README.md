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

### Via RemoteSettings

```csharp
var settings = new RemoteSettings
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

// Add included path
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "/Name/?",
    Fields = { new IndexField("Name") }
}, scope: "MyContainer");

// Add composite index
await indexManager.CreateAsync(new IndexDefinition
{
    Name = "composite_name_date",
    Fields =
    {
        new IndexField("Name"),
        new IndexField("CreatedDate", IndexFieldType.Descending)
    }
}, scope: "MyContainer");

// Add spatial index
await indexManager.AddSpatialIndexAsync("MyContainer", "/location/*", SpatialType.Point);

// Replace entire indexing policy
await indexManager.SetIndexingPolicyAsync("MyContainer", newPolicy);
```

## Partition Key

The default partition key path is `/id`. Change it before initialization:

```csharp
CosmosDBStore<Customer>.PartitionKeyPath = "/tenantId";
AsyncCosmosDBStore<Customer>.PartitionKeyPath = "/tenantId";
```

## Dependencies

- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Patterns
- Microsoft.Azure.Cosmos >= 3.46.1
