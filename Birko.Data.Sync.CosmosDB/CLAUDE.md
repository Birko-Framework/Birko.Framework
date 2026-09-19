# Birko.Data.Sync.CosmosDB

## Overview
Cosmos DB-specific sync knowledge item and store implementations for the Birko.Data.Sync framework.

## Project Location
`Birko.Data.Sync.CosmosDB/`

## Components

### Models
- `CosmosSyncKnowledgeItem` - Extends `AbstractModel`, implements `ISyncKnowledgeItem`

### Stores
- `CosmosSyncKnowledgeStore` - Sync store inheriting from `CosmosDBStore<CosmosSyncKnowledgeItem>`
- `AsyncCosmosSyncKnowledgeStore` - Async store inheriting from `AsyncCosmosDBStore<CosmosSyncKnowledgeItem>`

## Dependencies
- Birko.Data.Sync
- Birko.Data.CosmosDB

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or changed components.

### Test Requirements
Every new public functionality must have corresponding unit tests.
