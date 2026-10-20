# Birko.Data.Localization.MongoDB

## Overview
MongoDB support for Birko.Data.Localization (TASK-699): the indexes a translation collection needs, mirroring what
Birko.Data.Localization.SQL declares on SQL.

## Project Location
`C:\Source\Birko\Framework\Birko.Data.Localization.MongoDB\`

## Components
- **LocalizationMongoIndexes** — `Definitions(collection)` (the unique `UX_{collection}_Key` over EntityGuid, EntityType,
  FieldName, Culture, and `IX_{collection}_ValueHash`) and `EnsureAsync(AsyncMongoDBStore<TTranslation>)`, which creates
  each only if `ExistsAsync` says it is missing. The collection is `typeof(TTranslation).Name`, as `MongoDBClient.GetCollection<T>()`
  names it. Async store only: the sync `MongoDBStore<T>` requires a `MongoDBModel`, which translation models are not

## Rules the code keeps
- The unique key is a second line of defence behind the key Guid (TASK-698): it also refuses a duplicate under another
  Guid (a pre-TASK-698 row, a write around the upsert). Measured live: a race against a random-Guid writer leaves one row
  only with the index.
- A collection already holding a duplicate key is refused (`IndexManagementException`, E11000), never cleaned silently.

## Dependencies
- Birko.Data.Localization (IEntityTranslation), Birko.Data.MongoDB (stores, `MongoDBIndexManager`), Birko.Data.Patterns
  (`IndexDefinition`, `IndexField`, `IndexManagementException`)

## Maintenance
See the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
