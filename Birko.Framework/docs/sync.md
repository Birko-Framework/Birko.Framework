# Data Synchronization Guide

## Overview

Birko.Data.Sync synchronizes the entities of one store (local) with another (remote): one-way or bidirectional, in
batches, with conflict resolution and save filters. What it remembers between runs (the **knowledge**) lives in a
knowledge store of your choice.

## Core components

| Type | Role |
|------|------|
| `SyncProvider<TStore, T, TKnowledge>` / `AsyncSyncProvider<…>` | Runs a sync between two bulk stores (`Sync` / `SyncAsync`), or previews one (`Preview` / `PreviewAsync`) |
| `SyncOptions` | Direction, conflict policy, batch size, item cap, scope, callbacks |
| `SyncFilterOptions<T>` | Fetch predicates per side, and save filters (`CanSaveToLocal` / `CanSaveToRemote`) |
| `ISyncKnowledgeItemStore<TKnowledge>` / `IAsyncSyncKnowledgeItemStore<TKnowledge>` | Where knowledge is kept |
| `SyncQueue` | Runs syncs one at a time per scope, with a global concurrency limit |

Knowledge stores:

| Backend | Project | Store |
|---------|---------|-------|
| SQL | Birko.Data.Sync.Sql | `SqlSyncKnowledgeStore<DB>` / `AsyncSqlSyncKnowledgeStore<DB>` |
| ElasticSearch | Birko.Data.Sync.ElasticSearch | `AsyncElasticSyncKnowledgeStore` |
| MongoDB | Birko.Data.Sync.MongoDb | `AsyncMongoSyncKnowledgeStore` |
| JSON | Birko.Data.Sync.Json | `AsyncJsonSyncKnowledgeStore` |
| XML | Birko.Data.Sync.Xml | `AsyncXmlSyncKnowledgeStore` |
| RavenDB | Birko.Data.Sync.RavenDB | `RavenSyncKnowledgeStore` / `AsyncRavenSyncKnowledgeStore` |
| CosmosDB | Birko.Data.Sync.CosmosDB | `CosmosSyncKnowledgeStore` / `AsyncCosmosSyncKnowledgeStore` |

## Usage

```csharp
var provider = new AsyncSyncProvider<IAsyncBulkStore<Product>, Product, SqlSyncKnowledgeItem>(
    localStore, remoteStore, knowledgeStore);

var result = await provider.SyncAsync(new SyncOptions
{
    Scope = "products",
    Direction = SyncDirection.Bidirectional,          // or Download / Upload
    ConflictPolicy = ConflictResolutionPolicy.NewestWins, // LocalWins, RemoteWins, Custom
    BatchSize = 500,                                   // must be positive
    MaxItems = 10_000,                                 // optional cap per run
});

if (!result.Success) { /* result.Errors: one entry per item that failed */ }
```

## How a run decides

The **first run of a scope** (no knowledge yet) is an initial sync: it downloads what the local side lacks and leaves
everything else alone. `result.Direction` reports `Download` for it; your `SyncOptions` are not changed.

Every later run reads the scope's knowledge. A knowledge row records the version (the `UpdatedAt`) of each side when
the item was last **reconciled**, that is, present on both sides, or deleted on both. That record is what the decision
rests on:

| State | Decision |
|-------|----------|
| On both sides, neither changed since the record | Skip |
| On both sides, one side changed | That side wins, whatever the policy |
| On both sides, both changed | The policy: LocalWins / RemoteWins directly, NewestWins / Custom through a conflict carrying both items |
| On one side, never recorded on both | A new item: created on the other side |
| On one side, recorded on both | A deletion on the other side: propagated if the survivor did not change, otherwise the policy |

A model without `UpdatedAt` has no version, so it is always treated as changed.

Knowledge is written only for a reconciled state. An item that could not be reconciled (a save filter blocked the
write, a conflict resolved as `Skip`) leaves the previous record in place and is retried by the next run. Each run also
writes a scope marker row (an entry for `Guid.Empty`), so a run that reconciled nothing still ends the initial sync.

## Conflict resolution

`ResolveConflict` turns a conflict into `UseLocal`, `UseRemote` or `Skip`. With `ConflictPolicy.Custom`, your
`CustomConflictResolver` decides. `ConflictResolution.Merge` is **refused**: nothing in the framework can merge two
entities, so the item is reported in `result.Errors` instead of being silently left unresolved.

## Save filters

`CanSaveToLocal` / `CanSaveToRemote` guard every write to a side: create, update **and delete**. A blocked write
counts as skipped and is not recorded, so it is retried. `OnSaveFilterBlock` chooses what else happens: `Skip` (default),
`LogAsError` (through `OnError`), `MarkConflict` (through `OnConflict`) or `ThrowException` (the item fails).

## Item cap

With `MaxItems`, a run takes the items that need work first, then by Guid, so repeated capped runs converge on a
fully synced scope.

## SyncQueue

```csharp
var queue = new SyncQueue(maxConcurrentSyncs: 4);
await queue.EnqueueAsync("products", () => provider.SyncAsync(options));
```

Two syncs of one scope never run at the same time, whatever `maxConcurrentSyncs` is; it limits syncs of different
scopes. A cancelled wait leaves no trace in the queue.

## Tenant-Aware Sync

`TenantSyncProvider<TStore, T, TKnowledge>` runs this engine under one tenant, with any knowledge store above:

```csharp
var provider = new TenantSyncProvider<IAsyncBulkStore<Order>, Order, SqlSyncKnowledgeItem>(
    localStore, remoteStore, knowledgeStore, tenantContext);

await provider.SyncAsync(new TenantSyncOptions { Scope = "orders", TenantGuid = tenantId });
```

- **The tenant is resolved once per run**: `TenantSyncOptions.TenantGuid`, else the ambient tenant. Both present and
  different is refused (`TenantMismatchException`); neither present is refused for a tenant-scoped type
  (`TenantScopeRequiredException`), unless the run is inside an explicit all-tenants scope.
- **Only that tenant's rows are fetched and written.** The tenant term goes on both fetch predicates (enforced in
  process too, so a backend that widens a predicate cannot leak rows) and on every save, deletes included.
- **Knowledge is kept per tenant** by running under the scope `{scope}@tenant:{guid:N}`, so the knowledge stores need
  no tenant column. An all-tenants run, and a type with no `TenantGuid`, use the scope as given; their knowledge is
  separate from any tenant's.
- `TenantSyncQueue` runs different tenants concurrently and one tenant's syncs of a scope one at a time.

## See Also

- [Birko.Data.Sync](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Data.Sync)
- [Birko.Data.Sync.Tenant](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Data.Sync.Tenant)
