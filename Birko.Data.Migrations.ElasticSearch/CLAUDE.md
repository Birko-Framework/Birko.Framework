# Birko.Data.Migrations.ElasticSearch

## Overview
ElasticSearch migration backend using ElasticClient (NEST). Implements platform-agnostic IMigrationContext.

## Project Location
`C:\Source\Birko.Data.Migrations.ElasticSearch\`

## Components

### Runner
- `ElasticSearchMigrationRunner` — Takes `ElasticClient` (from `store.Connector`).

### Context
- `ElasticSearchMigrationContext` — Wraps ElasticClient. Schema and Data properties. Raw() exposes ElasticClient.
- `ElasticSearchSchemaBuilder` — CreateCollection creates index with mapping. AddField updates mapping via PUT. DropField is no-op (ES can't remove fields). CreateIndex creates aliases.
- `ElasticSearchDataMigrator` — UpdateDocuments via _update_by_query, DeleteDocuments via _delete_by_query, CopyData via _reindex.

### Store
- `ElasticSearchMigrationStore` — Stores migration state in an ElasticSearch index.

### Settings
- `ElasticSearchMigrationSettings` — MigrationsIndex, UseAliases, NumberOfShards, NumberOfReplicas

## Usage

```csharp
var runner = new ElasticSearchMigrationRunner(store.Connector);
runner.Register(new CreateProductsIndex());
runner.Migrate();
```

## Dependencies
- Birko.Data.Migrations
- Birko.Data.Patterns
- Birko.Data.ElasticSearch
- NEST / Elasticsearch.Net

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
