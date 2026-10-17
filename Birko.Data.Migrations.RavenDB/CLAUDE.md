# Birko.Data.Migrations.RavenDB

## Overview
RavenDB migration backend using IDocumentStore. Implements platform-agnostic IMigrationContext.

## Project Location
`Birko.Data.Migrations.RavenDB/`

## Components

### Runner
- `RavenMigrationRunner` — Takes `IDocumentStore` (from `store.DocumentStore`).

### Context
- `RavenDBMigrationContext` — Wraps IDocumentStore. Schema and Data properties. Raw() exposes IDocumentStore.
- `RavenDBSchemaBuilder` — CreateCollection is no-op (auto-created). AddField is a no-op; DropField deletes the field by patch-by-query (TASK-655). CreateIndex uses PutIndexesOperation.
- `RavenDBDataMigrator` — UpdateDocuments via PatchByQueryOperation, DeleteDocuments via DeleteByQueryOperation.
- Every name reaching RQL text has one producer here (TASK-640): `RqlField` refuses a field (filter term, update key,
  `RenameField`'s names) that is not a plain dotted identifier path, and `RqlCollection` makes a collection one escaped
  string literal (`\` and `'`), so `O'Brien` works and a crafted name cannot add a `WHERE`. Values are `$` parameters.

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
- Birko.Data.RavenDB (Settings base class)
- RavenDB.Client

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
