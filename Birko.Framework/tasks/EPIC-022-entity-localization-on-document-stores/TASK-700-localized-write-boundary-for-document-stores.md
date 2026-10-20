---
id: TASK-700
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
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

# A localized write boundary for MongoDB, RavenDB and Cosmos DB

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). `ILocalizedWriteBoundary` has one atomic implementation,
`SqlLocalizedWriteBoundary` (TASK-683). Any other pairing is best effort, so a failed translation write leaves the
entity written. MongoDB (`MongoDbUnitOfWork`), RavenDB (`RavenDbUnitOfWork`, cluster-wide session) and Cosmos DB
(`CosmosDbUnitOfWork`, a single-partition transactional batch) each have a unit of work.

## Acceptance criteria

- [x] Measure first what each unit of work can hold: both collections or containers in one transaction? Cosmos's batch
      is limited to one partition key, which may rule it out for an entity and its translations.
- [ ] A boundary per provider that can hold both, joining a caller's unit of work and refusing or degrading where it
      cannot. Each decision is written down.
- [ ] Live tests per provider: a failed translation write rolls the entity write back.

## Out of scope

- ElasticSearch, JSON, XML, InMemory: no transactions.

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Decided by the user 2026-10-20: MongoDB and RavenDB. Cosmos DB stays best effort (below)._

1. `AmbientSession<TSession>` (Birko.Data.Stores): a flow-scoped session keyed by database, the mechanics of
   `AmbientSqlTransaction`, with rollback-only marking. The document stores use their own context, else the ambient one.
2. **MongoDB** (done, committed separately):
   - stores route every driver call through `Session` (own `TransactionContext`, else ambient); `IMongoConnectedStore`;
   - `MongoDbUnitOfWork` publishes its session from `BeginAsync`, and refuses a commit a failed participant marked;
   - `MongoLocalizedWriteBoundary` in Birko.Data.Localization.MongoDB, mirroring `SqlLocalizedWriteBoundary`.
3. **RavenDB**: the same over `IAsyncDocumentSession` / `IDocumentSession`, a new Birko.Data.Localization.RavenDB.
4. **Cosmos DB**: a transactional batch holds one partition key of one container, and an entity and its translations
   are two containers. Impossible by construction; best effort, documented.

## Progress

**MongoDB — measured live (replica set rs0, MongoDB 7):**
- One store's session carries a second store's writes (each store builds its own client) when both are on the same
  settings: 0 rows left after the rollback (`SessionAcrossClientsMeasurementTests`).
- A failed translation write rolls the entity create, update (single, collection, filter) and delete (single,
  collection, filter) back; the sync wrapper does too.
- Inside a caller's `MongoDbUnitOfWork` the write joins; a failure makes `CommitAsync` throw
  `TransactionRollbackOnlyException` and the unit stays active for `RollbackAsync`.
- **A race inside a transaction is a write conflict, never a duplicate key**, even when the other writer committed
  between our read and our create: our snapshot predates its commit. MongoDB labels it `TransientTransactionError`; the
  boundary re-runs once and updates the other writer's row. So `StoreUniqueViolationException` is not retried (rule 53):
  inside a transaction it means a key held before it began that the write cannot see.
- A store in the unit of work's flow now joins it (`MongoTransactionBoundaryLiveTests` pins both: the same flow joins,
  another flow sees only committed rows).
