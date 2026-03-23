# Birko.Data.Migrations.CosmosDB

## Overview
Cosmos DB-specific migration framework for managing containers, indexing policies, and documents.

## Project Location
`C:\Source\Birko.Data.Migrations.CosmosDB\`

## Components

### Migration Base Class
- `CosmosMigration` - Extends `AbstractMigration` with `Database` parameter
  - Container helpers: `CreateContainer()`, `DeleteContainer()`
  - Indexing helpers: `AddIncludedPath()`, `AddExcludedPath()`, `AddCompositeIndex()`, `SetIndexingPolicy()`
  - Document helpers: `LoadDocument()`, `StoreDocument()`, `DeleteDocument()`, `DocumentExists()`, `BulkInsert()`

### Store
- `CosmosMigrationStore` - Implements `IMigrationStore`, stores state in a Cosmos DB document

### Runner
- `CosmosMigrationRunner` - Extends `AbstractMigrationRunner` with `Database` field

## Dependencies
- Birko.Data.Migrations
- Microsoft.Azure.Cosmos

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or changed components.

### Test Requirements
Every new public functionality must have corresponding unit tests.
