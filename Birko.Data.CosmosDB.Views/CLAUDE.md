# Birko.Data.CosmosDB.Views

## Overview
Azure Cosmos DB (NoSQL API) implementation of the Birko.Data.Views interfaces, providing read-only view queries over Cosmos DB containers.

## Project Location
`C:\Source\Birko.Data.CosmosDB.Views\`

## Purpose
- Read-only view queries over Cosmos DB containers via `IViewStore<TView>`
- View lifecycle management via `IViewManager`
- LINQ-based queries for non-aggregate views
- Cosmos SQL with GROUP BY for aggregate views

## Components

### CosmosViewStore\<TView\>
- Implements `IViewStore<TView>` for querying views
- Non-aggregate views: LINQ via `container.GetItemLinqQueryable<T>()` with Where/OrderBy/Skip/Take, executed via `ToFeedIterator`
- Aggregate views: Cosmos SQL with GROUP BY via `QueryDefinition` and `GetItemQueryIterator<TView>`
- Field names use property names as-is (matching SDK default serialization)
- Aggregate SQL built via shared `CosmosAggregationHelper` from `Birko.Data.CosmosDB`
- Joins are ignored (Cosmos DB does not support cross-container joins)

### CosmosViewManager
- Implements `IViewManager` for persistent view lifecycle
- `EnsureAsync`: No-op (Cosmos DB has no native server-side views)
- `DropAsync`: Deletes the named container via `DeleteContainerAsync`
- `ExistsAsync`: Reads container properties, returns true/false based on NotFound exception
- `RefreshAsync`: No-op (views are computed on-the-fly)

### CosmosFilterTranslator
- Internal helper that translates `Expression<Func<T, bool>>` filters into Cosmos SQL WHERE clauses
- Supports binary comparisons (==, !=, <, >, <=, >=), logical AND/OR, NOT, and string Contains

## Dependencies
- Birko.Data.Views (IViewStore, IViewManager, ViewDefinition)
- Birko.Data.Stores (OrderBy, OrderByHelper, AggregateFunction)
- Birko.Data.CosmosDB (CosmosAggregationHelper)
- Microsoft.Azure.Cosmos (Container, Database, QueryDefinition)

## Important Notes
- Cosmos DB does not support cross-container joins; join clauses in ViewDefinition are ignored
- Cosmos DB does not support native server-side views; all queries are computed on-the-fly
- Aggregate queries build raw Cosmos SQL; non-aggregate queries use the LINQ provider
- A true materialized view would require change-feed-based pipelines (Azure Functions), which is beyond scope

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or changed components.
