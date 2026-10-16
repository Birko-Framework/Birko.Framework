# Birko.Data.RavenDB.Views

## Overview
RavenDB view implementation for the Birko data layer. Translates portable `ViewDefinition` into RavenDB static indexes (Map/Reduce) and provides query access via `IViewStore<TView>` and lifecycle management via `IViewManager`.

## Project Location
`Birko.Data.RavenDB.Views/`

## Components

### RavenViewTranslator
- Static class that converts `ViewDefinition` into RavenDB Map/Reduce strings
- Map: `from entity in docs.{Collection} select new { ... }`, the collection named by the store's conventions (`Order` is in `Orders`)
- Joins: `LoadDocument("{RightCollection}/" + key, "{RightCollection}")`, on the right model's `Guid` only (a document's id is `RavenDocumentId.For`); Inner adds `where joined != null`; other join types refused
- Avg divides as `double`
- Reduce: groups results and applies Sum/Count/Min/Max/Avg aggregates
- Non-aggregate views produce Map only (no Reduce)

### RavenViewStore\<TView\>
- Implements `IViewStore<TView>`
- **OnTheFly** mode: refused (`NotSupportedException`) — a RavenDB view is a static index (TASK-151)
- **Persistent/Auto** mode: queries a static index via `session.Query<TView>(indexName)`; Auto reports a missing index by name
- A map-only index stores its fields and is read with `ProjectInto<TView>()`
- Supports filter, orderBy, skip/take pagination
- Uses shared `OrderByHelper.ApplyTo()` for dynamic ordering

### RavenViewManager
- Implements `IViewManager`
- `EnsureAsync`: creates a static index via `PutIndexesOperation` using translated Map/Reduce
- `DropAsync`: removes an index via `DeleteIndexOperation`
- `ExistsAsync`: checks index existence via `GetIndexOperation`
- `RefreshAsync`: no-op (RavenDB indexes are auto-maintained)

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager)
- Birko.Data.Stores (OrderBy\<T\>, OrderByHelper, AggregateFunction)
- RavenDB.Client

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, new dependencies, or updated interfaces.
