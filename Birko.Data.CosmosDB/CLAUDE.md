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

RemoteSettings mapping:
- `Location` = connection string or endpoint URL
- `Name` = database name
- `Password` = account key (when using endpoint URL)
- `UserName` = container name (optional, defaults to type name)

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Patterns (UnitOfWork, IndexManagement)
- Microsoft.Azure.Cosmos v3.46.1

## Important Notes
- Transactional batches are scoped to a single partition key
- Default partition key path is "/id" — configurable via `PartitionKeyPath` static property
- Bulk execution is enabled by default via `AllowBulkExecution = true`
- Sync store operations use `.GetAwaiter().GetResult()` wrappers; prefer async store for production

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
