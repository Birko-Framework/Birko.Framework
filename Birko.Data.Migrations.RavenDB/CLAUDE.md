# Birko.Data.Migrations.RavenDB

## Overview
RavenDB migration backend using IDocumentStore. Implements platform-agnostic IMigrationContext.

## Project Location
`C:\Source\Birko.Data.Migrations.RavenDB\`

## Components

### Runner
- `RavenMigrationRunner` — Takes `IDocumentStore` (from `store.DocumentStore`).

### Context
- `RavenDBMigrationContext` — Wraps IDocumentStore. Schema and Data properties. Raw() exposes IDocumentStore.
- `RavenDBSchemaBuilder` — CreateCollection is no-op (auto-created). AddField/DropField are no-op. CreateIndex uses PutIndexesOperation.
- `RavenDBDataMigrator` — UpdateDocuments via PatchByQueryOperation, DeleteDocuments via DeleteByQueryOperation.

### Store
- `RavenMigrationStore` — Stores migration state in a single RavenDB document.

## Usage

```csharp
var runner = new RavenMigrationRunner(store.DocumentStore);
runner.Register(new CreateIndexes());
runner.Migrate();
```

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- RavenDB.Client

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
