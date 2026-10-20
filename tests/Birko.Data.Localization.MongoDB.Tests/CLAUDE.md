# Birko.Data.Localization.MongoDB.Tests

## Project Location
`tests/Birko.Data.Localization.MongoDB.Tests/`

## Structure
- `LocalizationMongoIndexesLiveTests.cs` — TASK-699, live MongoDB: a value lookup's query plan before (`COLLSCAN`) and
  after (`IXSCAN` on the ValueHash index) `EnsureAsync`; both indexes read back from the server, the key unique, in key
  order, idempotent; a duplicate key under another Guid refused typed; a race against a random-Guid writer leaves one
  row; a collection already holding a duplicate refused; a store without settings refused.

## Test Patterns
- Each test owns a fresh database (`birko_task699_*`), dropped in `Dispose` — never a shared one (rule 61).
