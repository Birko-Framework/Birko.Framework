# Birko.Data.CosmosDB

## Overview
Azure Cosmos DB (NoSQL API) implementation for the Birko data layer providing document-based storage with transactional batch support.

## Project Location
`C:\Source\Birko.Data.CosmosDB\`

## Purpose
- Document-based storage via Azure Cosmos DB NoSQL API
- Transactional batch operations (scoped to partition key)
- LINQ-based queries via Microsoft.Azure.Cosmos SDK v3
- Bulk execution for high-throughput scenarios

## Components

### Stores
- `CosmosDBStore<T>` - Synchronous Cosmos DB store
- `AsyncCosmosDBStore<T>` - Asynchronous Cosmos DB store with transactional batch support

### Repositories
- `CosmosDBRepository<TViewModel, TModel>` - ViewModel repository
- `AsyncCosmosDBRepository<TViewModel, TModel>` - Async ViewModel repository
- `CosmosDBModelRepository<T>` - Direct model repository
- `AsyncCosmosDBModelRepository<T>` - Async direct model repository

### UnitOfWork
- `CosmosDbUnitOfWork` - Wraps `TransactionalBatch` (partition-key scoped)

### IndexManagement
- `CosmosDBIndexManager` - Manages indexing policies (included paths, composite indexes, spatial indexes)

## Connection

### Settings (Birko.Data.CosmosDB.Stores.Settings)
Typed settings class extending `RemoteSettings`:
- `PartitionKeyPath` (default: "/id") — Cosmos DB partition key path
- `RequestTimeout` (default: 30 seconds) — request timeout
- `AllowBulkExecution` (default: true) — enables bulk execution mode
- `GetCosmosClientOptions()` — builds `CosmosClientOptions` from settings

Settings mapping from `RemoteSettings`:
- `Location` = connection string or endpoint URL
- `Name` = database name
- `Password` = account key (when using endpoint URL)
- `UserName` = container name (optional, defaults to type name)

### Legacy Settings (still supported)
`SetSettings(ISettings)` accepts `Birko.Configuration.RemoteSettings` and wraps it into a `Settings` instance with defaults.

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Patterns (UnitOfWork, IndexManagement)
- Microsoft.Azure.Cosmos v3.46.1

## Important Notes
- Transactional batches are scoped to a single partition key
- Default partition key path is "/id" — configurable via `Settings.PartitionKeyPath`
- Bulk execution is enabled by default via `Settings.AllowBulkExecution = true`
- Sync store operations use `.GetAwaiter().GetResult()` wrappers; prefer async store for production

## Transaction boundary (TASK-240)

`CosmosDbUnitOfWork` wraps a `TransactionalBatch`: **atomic, but scoped to ONE logical partition key**.

- **In practice that means one entity.** `AsyncCosmosDBStore<T>` derives an item's partition key from its
  `Guid`, so **every document is its own logical partition** and a boundary spanning two entities is
  impossible by construction -- whatever the API lets you type. `Capabilities.Scope` is `SinglePartition`.
- **The limit is enforced at the call site**, not left to the server. Adding a second entity throws
  `CosmosTransactionScopeException` naming both partition keys. Previously the second `CreateItem` was
  accepted silently and the whole batch failed at `ExecuteAsync` with an opaque BadRequest -- or, if the
  caller never inspected the response, the writes were simply lost. Enforced on the whole verb family
  (create / update / upsert / delete, single and bulk), because a refused create beside an escaping delete
  is the same defect wearing a quieter coat.
- **Reads do NOT see the batch's own writes.** Operations are buffered client-side until `ExecuteAsync` and
  the batch exposes no read, so read-then-write logic inside a Cosmos boundary reads the pre-transaction
  state. `Capabilities.ReadsSeeUncommittedWrites` is `false`. This one is not fixable -- it is what a
  `TransactionalBatch` is.
- `SetTransactionContext(null)`, or a new batch, resets the pinned partition.

Pinned by `Birko.Data.CosmosDB.Tests.CosmosTransactionBoundaryTests` (7). **Deliberately not gated**: the
guard and the batch are both client-side, so a `Container` handle that never contacts a server exercises
them end to end -- gating the one assertion that matters is the failure mode this work exists to remove.
Mutation-tested: disabling the guard fails 3 of 61.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or changed components.

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
