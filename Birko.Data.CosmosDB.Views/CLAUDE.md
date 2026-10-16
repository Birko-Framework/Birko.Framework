# Birko.Data.CosmosDB.Views

## Overview
Azure Cosmos DB (NoSQL API) implementation of the Birko.Data.Views interfaces, providing read-only view queries over Cosmos DB containers.

## Project Location
`Birko.Data.CosmosDB.Views/`

## Purpose
- Read-only view queries over Cosmos DB containers via `IViewStore<TView>`
- View lifecycle management via `IViewManager`
- Cosmos SQL projection (`SELECT c["Source"] AS ViewProp`) for non-aggregate views (TASK-151: LINQ ignored renames)
- Cosmos SQL with GROUP BY for aggregate views

## Components

### CosmosViewStore\<TView\>
- Implements `IViewStore<TView>` for querying views
- Non-aggregate views: a Cosmos SQL projection with WHERE/ORDER BY/OFFSET LIMIT on the source fields the view properties select; `CountAsync` is `SELECT VALUE COUNT(1)`
- Grouping views (aggregates or GroupBy): `QueryFirstAsync` and `CountAsync` take the GROUP BY path too; the count is the number of groups the view's own query returns
- A filter on an aggregate view property is refused (it would need HAVING); `Persistent` mode is refused (no stored views)
- Aggregate views: Cosmos SQL with GROUP BY via `QueryDefinition` and `GetItemQueryIterator<TView>`
- Field names use property names as-is (matching SDK default serialization)
- Aggregate SQL built via shared `CosmosAggregationHelper` from `Birko.Data.CosmosDB`
- Joins are refused in the constructor (`NotSupportedException`); Cosmos DB has no cross-document join

### CosmosViewManager
- Implements `IViewManager` for persistent view lifecycle
- `EnsureAsync`: refuses a Persistent definition or a join; otherwise nothing to create (views are computed on the fly)
- `DropAsync`: no-op — no container is ever a view, and deleting one deleted data (TASK-151)
- `ExistsAsync`: always false, for the same reason
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
- Cosmos DB does not support cross-container joins; join clauses in ViewDefinition are refused
- Cosmos DB does not support native server-side views; all queries are computed on-the-fly
- Every query is raw, parameterised Cosmos SQL
- A true materialized view would require change-feed-based pipelines (Azure Functions), which is beyond scope

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or changed components.
