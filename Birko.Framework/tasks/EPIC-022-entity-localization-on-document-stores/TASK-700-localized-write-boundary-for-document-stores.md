---
id: TASK-700
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

# A localized write boundary for MongoDB, RavenDB and Cosmos DB

## Context

Filed 2026-10-20 (EPIC-022, chosen by the user). `ILocalizedWriteBoundary` has one atomic implementation,
`SqlLocalizedWriteBoundary` (TASK-683). Any other pairing is best effort, so a failed translation write leaves the
entity written. MongoDB (`MongoDbUnitOfWork`), RavenDB (`RavenDbUnitOfWork`, cluster-wide session) and Cosmos DB
(`CosmosDbUnitOfWork`, a single-partition transactional batch) each have a unit of work.

## Acceptance criteria

- [x] Measure first what each unit of work can hold: both collections or containers in one transaction? Cosmos's batch
      is limited to one partition key, which may rule it out for an entity and its translations.
- [x] A boundary per provider that can hold both, joining a caller's unit of work and refusing or degrading where it
      cannot. Each decision is written down.
- [x] Live tests per provider: a failed translation write rolls the entity write back.

## Out of scope

- ElasticSearch, JSON, XML, InMemory: no transactions.

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Decided by the user 2026-10-20: MongoDB and RavenDB. Cosmos DB stays best effort (below)._

1. `AmbientSession<TSession>` (Birko.Data.Stores): a flow-scoped session keyed by database, the mechanics of
   `AmbientSqlTransaction`, with rollback-only marking. The document stores use their own context, else the ambient one.
2. **MongoDB** (committed separately, 0be6981c):
   - stores route every driver call through `Session` (own `TransactionContext`, else ambient); `IMongoConnectedStore`;
   - `MongoDbUnitOfWork` publishes its session from `BeginAsync`, and refuses a commit a failed participant marked;
   - `MongoLocalizedWriteBoundary` in Birko.Data.Localization.MongoDB, mirroring `SqlLocalizedWriteBoundary`.
3. **RavenDB**: the same over `IAsyncDocumentSession` / `IDocumentSession`, a new Birko.Data.Localization.RavenDB.
   It needed [[TASK-702]] first (spawned and committed separately, e51ccf0e).
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

**RavenDB — measured live (RavenDB 7.2):**
- Both stores write into one cluster-wide session; a failed translation write discards the entity create, update
  (single, collection, filter) and delete (single, collection, filter); the sync wrapper does too.
- Writes through the boundary commit, and a later update of them through it commits (needs TASK-702: before it, any
  update in a cluster-wide session of a document a cluster transaction wrote failed at save).
- Inside a caller's `RavenDbUnitOfWork` the write joins its session; a failure makes `CommitAsync` throw
  `TransactionRollbackOnlyException`, and the session stays for `RollbackAsync`.
- A writer saving the same key between our existence check and our save makes the save fail with
  `ClusterTransactionConcurrencyException` (a `ConcurrencyException`); the write re-runs once and updates its row.
- A store in the unit of work's flow joins it; a store in another flow saves on its own
  (`RavenTransactionBoundaryLiveTests`).
- The database key is `RavenAmbientKey.Of(documentStore)` (URLs and database, one producer), because a unit of work and
  a store built from a URL have no settings.

**Cosmos DB:** best effort, documented in the Localization README's pairing table and docs/localization.md. Its
transactional batch holds one partition key of one container, and an entity and its translations are two containers.

## Outcome

Done 2026-10-20. MongoDB and RavenDB have an atomic localized write boundary; Cosmos DB stays best effort.

**Tests:**
- `MongoLocalizedWriteBoundaryLiveTests` (12), `SessionAcrossClientsMeasurementTests` (1), and a joining test in
  `MongoTransactionBoundaryLiveTests`;
- `RavenLocalizedWriteBoundaryLiveTests` (11), and a joining test in `RavenTransactionBoundaryLiveTests`.

**Mutations, all red:** MongoDB 9 (one test fixed on the way, where a leaked create and a leaked delete cancelled out);
RavenDB 9 (the boundary's own-session mutation re-run in a form that removes the session; the first form was a no-op).

**Regression:** every suite importing Birko.Data.MongoDB or Birko.Data.RavenDB, Birko.Data.Localization.Tests and
Birko.Data.Patterns.Tests, live; the solution and the Sandbox build.
