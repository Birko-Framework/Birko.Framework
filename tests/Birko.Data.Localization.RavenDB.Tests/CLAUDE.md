# Birko.Data.Localization.RavenDB.Tests

## Project Location
`tests/Birko.Data.Localization.RavenDB.Tests/`

## Structure
- `RavenLocalizedWriteBoundaryLiveTests.cs` — TASK-700, live RavenDB: a failed translation write discards the entity
  create, update and delete (async and sync wrappers); writes through the boundary commit and can be updated again
  (TASK-702); best effort across two databases; a caller's `RavenDbUnitOfWork` joined, and refused a commit after a
  failed joined write; a session given to the entity store carries both writes; a writer saving the key before our save
  makes it fail and the write re-run once.

## Test Patterns
- Each test owns a fresh database (`birko-task700-*`), hard-deleted in `Dispose` — never a shared one (rule 61).
- Assertions load by id; a query reads an index that catches up later. Filter writes wait for it (`SettleAsync`).
