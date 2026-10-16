# Birko.Data.ElasticSearch

Elasticsearch implementation for the Birko Framework providing full-text search and document storage.

## Features

- Full-text search with tokenization, stemming, and relevance scoring
- Document-based CRUD operations (sync/async)
- Bulk operations optimized for Elasticsearch
- Aggregations and pagination
- Search result highlighting
- Index mapping and management

## Installation

```bash
dotnet add package Birko.Data.ElasticSearch
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- NEST (Elasticsearch .NET client)

## Usage

```csharp
using Birko.Data.ElasticSearch.Stores;

var settings = new ElasticSearchSettings
{
    Uri = "http://localhost:9200",
    IndexName = "products",
    Username = "elastic",
    Password = "password"
};

var store = new ElasticSearchStore<Product>(settings);
var id = store.Create(product);
```

### Search

```csharp
var response = Client.Search<Product>(s => s
    .Index(Settings.IndexName)
    .Query(q => q.MultiMatch(m => m
        .Query("search term")
        .Fields(f => f.Field(p => p.Name).Field(p => p.Description))
    ))
    .From(0).Size(20)
    .Sort(sort => sort.Descending(f => f.CreatedAt))
);
```

## API Reference

### Stores

- **ElasticSearchStore\<T\>** - Sync store
- **ElasticSearchBulkStore\<T\>** - Bulk operations
- **AsyncElasticSearchStore\<T\>** - Async store
- **AsyncElasticSearchBulkStore\<T\>** - Async bulk store

### Repositories

- **ElasticSearchRepository\<T\>** / **ElasticSearchBulkRepository\<T\>**
- **AsyncElasticSearchRepository\<T\>** / **AsyncElasticSearchBulkRepository\<T\>**

### Index Management

```csharp
using Birko.Data.ElasticSearch.IndexManagement;

var manager = new IndexManager(client);

// Create an index with settings and mappings
await manager.CreateIndexAsync("products-v2", new
{
    settings = new { number_of_shards = 2, number_of_replicas = 1 },
    mappings = new { properties = new { Name = new { type = "text" } } }
});

// Get index information
IndexInfo info = await manager.GetIndexInfoAsync("products-v2");
Console.WriteLine($"Docs: {info.DocumentCount}, Size: {info.Size}");

// Manage aliases
await manager.AddAliasAsync("products-v2", "products");
await manager.RemoveAliasAsync("products-v1", "products");

// Refresh, flush, clear cache
await manager.RefreshIndexAsync("products-v2");
await manager.FlushIndexAsync("products-v2");
await manager.ClearCacheAsync("products-v2");
```

### Uniform IIndexManager Interface

```csharp
using Birko.Data.ElasticSearch.IndexManagement;
using Birko.Data.Patterns.IndexManagement;

// Adapter wraps existing IndexManager with IIndexManager interface
var adapter = new ElasticSearchIndexManagerAdapter(client);

// Same API as MongoDB, RavenDB, SQL providers
await adapter.CreateAsync(new IndexDefinition
{
    Name = "products-v2",
    Properties = new Dictionary<string, object>
    {
        ["NumberOfShards"] = 2,
        ["NumberOfReplicas"] = 1
    }
});
var indexes = await adapter.ListAsync();
var info = await adapter.GetInfoAsync("products-v2");

// Access full ES-specific capabilities via .Native
var native = adapter.Native;
await native.CreateAliasAsync("products-v2", "products");
await native.SwapAliasAsync("products", "products-v1", "products-v2");
```

### Mapping drift and its repair

An index is mapped once, so a model that changes later can query fields the index does not have (they match nothing,
silently). `DetectMappingDriftAsync()` reports `Missing`, `Unexpected` and `TypeMismatch` fields; `PlanMappingRepairAsync()`
turns the report into requests you review and send yourself:

```csharp
var plan = await store.PlanMappingRepairAsync();
foreach (var request in plan.Requests) Console.WriteLine(request);  // PUT /<index>/_mapping {"properties":{...}}
foreach (var skip in plan.NotActioned) Console.WriteLine(skip);     // type changes: reindex; undeclared fields
```

A missing field is added in place. Documents indexed before the repair have no value for it, so they read back with the
property's default and match no filter on it. A type change needs a new index and a reindex (below).

### Reindexing

```csharp
using Birko.Data.ElasticSearch.IndexManagement;

var reindexHelper = new ReindexHelper(client);

// Basic reindex
ReindexResult result = await reindexHelper.ReindexAsync("products-v1", "products-v2");
Console.WriteLine($"Indexed {result.DocumentsProcessed} docs in {result.Duration}");

// Started without waiting: pending, not a success. Follow the task, then refresh the target.
ReindexResult started = await reindexHelper.ReindexAsync("products-v1", "products-v2", waitForCompletion: false);
if (started.IsPending) Console.WriteLine($"Running as task {started.TaskId}");

// Reindex with a Painless script transformation
ReindexResult scriptResult = await reindexHelper.ReindexWithScriptAsync(
    "products-v1", "products-v2",
    "ctx._source.price = ctx._source.price * 1.1"
);

// Zero-downtime reindex via alias swap
ReindexResult swapResult = await reindexHelper.ZeroDowntimeReindexAsync(
    "products-v1", "products-v2", "products"
);
```

### Search Result Highlighting

Highlight matching terms in search results:

```csharp
using Birko.Data.ElasticSearch.Stores;

var response = Client.Search<Product>(s => s
    .Index(Settings.IndexName)
    .Query(q => q.MultiMatch(m => m
        .Query("search term")
        .Fields(f => f.Field(p => p.Name).Field(p => p.Description))
    ))
    .Highlight(h => h
        .PreTags("<em>")
        .PostTags("</em>")
        .Fields(
            f => f.Field(p => p.Name),
            f => f.Field(p => p.Description)
        )
    )
);

foreach (var hit in response.Hits)
{
    var highlights = hit.Highlight;
    if (highlights.ContainsKey("name"))
    {
        Console.WriteLine($"Name: {string.Join("...", highlights["name"])}");
    }
}
```

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.ElasticSearch.ViewModel](../Birko.Data.ElasticSearch.ViewModel/) - ViewModel repositories

## Filter-Based Bulk Operations

ElasticSearch stores support native filter-based update and delete:

- `Update(filter, PropertyUpdate<T>)` — Uses `UpdateByQuery` with Painless scripts generated from property assignments
- `Delete(filter)` — Uses `DeleteByQuery` with filter converted via `ParseExpression()`
- `Update(filter, Action<T>)` — Read-modify-save fallback for complex mutations

### Filter translation notes

- **A filter that cannot be translated throws** `NotSupportedException` — it never widens to match-all. This
  is enforced on every filter→query conversion, reads and by-query writes alike; the destructive paths
  additionally reject a null filter with `ArgumentNullException`.
- **Empty membership sets match nothing.** `ids.Contains(x.Field)` with an empty or null collection emits
  `MatchNoneQuery` (previously it dropped the clause, silently returning wrong rows); negated, it matches
  everything.
- Predicates also accept ternaries, `??`, and column arithmetic (the latter via a guarded Painless script
  query) — see [CLAUDE.md § Filter translation](CLAUDE.md) for the full supported surface and its caveats.

## License

Part of the Birko Framework.
