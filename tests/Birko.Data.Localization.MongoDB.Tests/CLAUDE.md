# Birko.Data.Localization.MongoDB.Tests

## Project Location
`tests/Birko.Data.Localization.MongoDB.Tests/`

## Structure
- `LocalizationMongoIndexesLiveTests.cs` — TASK-699, live MongoDB: a value lookup's query plan before (`COLLSCAN`) and
  after (`IXSCAN` on the ValueHash index) `EnsureAsync`; both indexes read back from the server, the key unique, in key
  order, idempotent; a duplicate key under another Guid refused typed; a race against a random-Guid writer leaves one
  row; a collection already holding a duplicate refused; a store without settings refused.
- `MongoLocalizedWriteBoundaryLiveTests.cs` — TASK-700, live MongoDB (replica set): a failed translation write rolls the
  entity create, update and delete back (async and sync wrappers); best effort across two databases; a caller's
  `MongoDbUnitOfWork` joined, and refused a commit after a failed joined write; a session given to the entity store
  carries both writes; a race (a writer committing between our read and create, and one holding an open transaction) is
  a write conflict re-run once; a key held out of sight is not re-run.
- `SessionAcrossClientsMeasurementTests.cs` — TASK-700's first measurement: one store's session carries a second store's
  writes on the same settings, though each store builds its own client.

## Test Patterns
- Each test owns a fresh database (`birko_task699_*`, `birko_task700_*`), dropped in `Dispose` — never a shared one (rule 61).
