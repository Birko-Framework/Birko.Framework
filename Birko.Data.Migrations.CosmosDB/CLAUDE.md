# Birko.Data.Migrations.CosmosDB

## Overview
Cosmos DB migration backend using Database. Implements platform-agnostic IMigrationContext.

## Project Location
`Birko.Data.Migrations.CosmosDB/`

## Components

### Runner
- `CosmosMigrationRunner` — Takes `Database` (from `store.Client` / `store.Container`).

### Context
- `CosmosDBMigrationContext` — Wraps Database. Schema and Data properties. Raw() exposes Database.
- `CosmosDBSchemaBuilder` — CreateCollection creates containers. AddField updates indexing policy included paths. DropIndex modifies IndexingPolicy.
- `CosmosDBDataMigrator` — UpdateDocuments via GetItemQueryIterator + ReplaceItemAsync, DeleteDocuments via DeleteStreamIterator.

### Store
- `CosmosMigrationStore` — Stores migration state in a Cosmos DB document.

## Usage

```csharp
var runner = new CosmosMigrationRunner(database);
runner.Register(new CreateContainer());
runner.Migrate();
```

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.CosmosDB (Settings base class)
- Microsoft.Azure.Cosmos

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
