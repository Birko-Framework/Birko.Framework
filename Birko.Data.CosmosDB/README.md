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

## Filters and null

A filter answers what the C# expression answers, also for a member that is null or missing from the stored document
(TASK-896). `x.N.HasValue`, `x.N == null`, `x.N != 5`, `!(x.N > 5)`, `ids.Contains(x.N)` with a null in the list,
`x.N ?? 0`, `x.N.GetValueOrDefault()` and `string.IsNullOrEmpty(x.S)` are rewritten before the SDK renders them
(`CosmosFilterRewrite.Prepare`, shared with `CosmosViewStore`). Ordinal string comparisons
(`string.CompareOrdinal(x.S, "b") > 0`, `string.Compare(x.S, "b", StringComparison.Ordinal) > 0`) translate.
Refused with `NotSupportedException` before any request: culture or ignore-case comparisons (`string.Compare(a, b)`,
`CompareTo`), `string.IsNullOrWhiteSpace`, and an ordinal comparison against a value holding a character at or above
U+D800.

A non-nullable member missing from a stored document (one written before the member existed) is read as its default,
as C# reads it: `x.Count == 0`, `!x.IsDeleted` and `x.Status == Status.New` match such a document (TASK-899). Nested
predicates (`x.Items.Any(i => i.Score == null)`) answer null and missing members the same way, and `All` is supported
(as `!Any(!p)`); an aggregate whose filter holds a nested predicate is refused. A `Contains` list holding `null` or the
default is split past the query limit like any other list.

## Filters and sorts on DateTime

A comparison or a sort of a `DateTime` member answers what C# answers, which compares the ticks whatever the kind
(TASK-902). The stored text (`…10:00:00Z`, `…10:00:00.5Z`, `…10:00:00+01:00`, `…10:00:00`) is compared by a wall-clock
key (the first 19 characters and the fraction padded to 7 digits), behind a plain text range the index can serve; a
sort keeps the server's order and reorders the rows that share a second, reading a run that crosses a page's edge
whole. A value stored with an offset is exact when the offset is the reader's own. Refused with
`NotSupportedException` before any request: a comparison, list or sort of a `DateTimeOffset` member, a member of a
stored DateTime (`x.T.Year`), and a DateTime computed from a member (`x.T.AddDays(1)`).
