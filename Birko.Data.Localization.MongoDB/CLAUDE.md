# Birko.Data.Localization.MongoDB

## Overview
MongoDB support for Birko.Data.Localization: the indexes a translation collection needs (TASK-699), and the write
boundary that makes an entity write and its translation writes one transaction (TASK-700), mirroring
Birko.Data.Localization.SQL.

## Project Location
`C:\Source\Birko\Framework\Birko.Data.Localization.MongoDB\`

## Components
- **LocalizationMongoIndexes** — `Definitions(collection)` (the unique `UX_{collection}_Key` over EntityGuid, EntityType,
  FieldName, Culture, and `IX_{collection}_ValueHash`) and `EnsureAsync(AsyncMongoDBStore<TTranslation>)`, which creates
  each only if `ExistsAsync` says it is missing. The collection is `typeof(TTranslation).Name`, as `MongoDBClient.GetCollection<T>()`
  names it. Async store only: the sync `MongoDBStore<T>` requires a `MongoDBModel`, which translation models are not
- **MongoLocalizedWriteBoundary** — `For(entityStore, translationStore, requireAtomic)` finds an `IMongoConnectedStore`
  with a client through each chain (`StoreChain.Walk`); equal `AmbientKey` (settings id) means atomic, else
  `BestEffortWriteBoundary`. `Run`/`RunAsync`: a store's own session is published and used; an ambient session for the
  database (a caller's `MongoDbUnitOfWork`) is joined and marked rollback-only on failure; otherwise its own transaction
  (sync: a session of its own; async: a `MongoDbUnitOfWork` built in the writing flow), committed on return and on
  `EntityNotFoundException`, aborted and re-run once on a `TransientTransactionError`

## Rules the code keeps
- The unique key is a second line of defence behind the key Guid (TASK-698): it also refuses a duplicate under another
  Guid (a pre-TASK-698 row, a write around the upsert). Measured live: a race against a random-Guid writer leaves one row
  only with the index.
- A collection already holding a duplicate key is refused (`IndexManagementException`, E11000), never cleaned silently.
- Inside a transaction a racing writer is a **write conflict, never a duplicate key** (measured: also when it committed
  between our read and our create, since our snapshot predates its commit). So the boundary retries only on the
  `TransientTransactionError` label. A `StoreUniqueViolationException` inside a transaction means a key held before it
  began that the write could not see; a re-run cannot change that, so it is not retried (rule 53).
- No per-caller state on the store objects (rule 36): the session reaches the stores through
  `AmbientSession<IClientSessionHandle>`, keyed by the database.

## Dependencies
- Birko.Data.Localization (IEntityTranslation, `ILocalizedWriteBoundary`), Birko.Data.MongoDB (stores,
  `MongoDBIndexManager`, `IMongoConnectedStore`, `MongoDbUnitOfWork`), Birko.Data.Stores (`AmbientSession`,
  `StoreChain`), Birko.Data.Patterns (`IndexDefinition`, `IndexField`, `IndexManagementException`)

## Maintenance
See the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
