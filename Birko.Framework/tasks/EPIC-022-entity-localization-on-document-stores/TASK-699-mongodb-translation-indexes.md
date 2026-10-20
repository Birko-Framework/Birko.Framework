---
id: TASK-699
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# MongoDB: declare the translation key and ValueHash indexes

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). On SQL, `EntityTranslationMapping` declares the unique key
`(EntityGuid, EntityType, FieldName, Culture)` and the `ValueHash` index (TASK-680/681). On MongoDB nothing declares
them, so lookups by value scan the collection. MongoDB can hold a unique index (`MongoDBIndexManager`), and its stores
already translate a duplicate key (E11000) into `StoreUniqueViolationException`, so the upsert's retry would work there
once the index exists.

## Acceptance criteria

- [x] One call creates both indexes on a MongoDB translation collection, idempotently, as `GrantSqlIndexes` does for
      SQL; whether this lives in a new project (`Birko.Data.Localization.MongoDB`) is decided first.
- [x] Live MongoDB: the indexes exist (read back from the server), a duplicate key is refused typed, a racing upsert
      leaves one row, and a lookup by value uses the index (explain plan).
- [x] Interplay with TASK-698 recorded: with deterministic Guids the unique index is a second line of defence.

## Out of scope

- Other document stores: they cannot hold a unique index (their managers refuse it).

## Human test plan

N/A — asserted by the live suite.

## Implementation plan

_Decided by the user 2026-10-20: a new project, Birko.Data.Localization.MongoDB, mirroring Birko.Data.Localization.SQL
(also the home of TASK-700's MongoDB boundary)._

1. `LocalizationMongoIndexes.Definitions(collection)` and `EnsureAsync(AsyncMongoDBStore<TTranslation>)` create each index
   only where `ExistsAsync` says it is missing (idempotent). The collection is the model's type name, as the stores
   name it.
2. Live tests measure the query plan before and after, read both indexes back, and pin a duplicate under another Guid,
   a race against a random-Guid writer, existing duplicates and an unconfigured store.

## Outcome

Done 2026-10-20. New shared project **Birko.Data.Localization.MongoDB**. It is registered in the slnx and the workspace
(project and tests), in the Sandbox aggregator, in README, CLAUDE-projects (catalog and Dependency Flow),
docs/dependencies.md, docs/localization.md, and the spec map's entity-localization area.

**Measured live (MongoDB 7):**
- Before `EnsureAsync`, a lookup by `ValueHash` is a `COLLSCAN`; after it, an `IXSCAN` on `IX_EntityTranslationModel_ValueHash`.
- Both indexes read back from the server. The key is unique and in key order, and a second call changes nothing.
- A duplicate key under another Guid is refused as `StoreUniqueViolationException`.
- A race against a writer that does not derive the key Guid leaves one row with the index, and two without it (the
  mutation). So the index is a real second line of defence behind TASK-698.
- A collection already holding a duplicate is refused with `IndexManagementException`; the README gives the remedy.

**Only the async store is supported:** the sync `MongoDBStore<T>` requires a `MongoDBModel`, which translation models
are not.

**Tests:** `LocalizationMongoIndexesLiveTests` (6).

**Mutations, all red:**
- unique flag dropped (4 red);
- value index not created (2 red);
- the race run without the indexes (1 red).

The solution and Sandbox build with no warnings in the new project.
