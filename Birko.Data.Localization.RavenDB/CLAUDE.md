# Birko.Data.Localization.RavenDB

## Overview
RavenDB support for Birko.Data.Localization (TASK-700): the write boundary that makes an entity write and its
translation writes one transaction, mirroring Birko.Data.Localization.SQL and Birko.Data.Localization.MongoDB.

## Project Location
`C:\Source\Birko\Framework\Birko.Data.Localization.RavenDB\`

## Components
- **RavenLocalizedWriteBoundary** — `For(entityStore, translationStore, requireAtomic)` finds an `IRavenConnectedStore`
  with a document store through each chain (`StoreChain.Walk`); equal `AmbientKey` (`RavenAmbientKey`: URLs and
  database) means atomic, else `BestEffortWriteBoundary`. `Run`/`RunAsync`: a store's own session is published and used;
  an ambient session for the database (a caller's `RavenDbUnitOfWork`) is joined and marked rollback-only on failure;
  otherwise a cluster-wide session of its own (sync: `OpenSession`; async: a `RavenDbUnitOfWork` built in the writing
  flow), saved on return and on `EntityNotFoundException`, discarded on failure, re-run once on a `ConcurrencyException`
  at save.

## Rules the code keeps
- Writes in a session reach the server only at save, so a write that throws leaves nothing behind once its session is
  discarded.
- A race surfaces at save, not during the write: another writer's cluster transaction moved the key's atomic guard
  (measured: `ClusterTransactionConcurrencyException`). A refusal during the write (`StoreUniqueViolationException`
  from the store's existence check) is retried in place by the translation upsert, so it is not re-run here (rule 53).
- Updates of documents a cluster transaction wrote depend on TASK-702 (the stores copy onto the tracked document).
- No per-caller state on the store objects (rule 36): the session reaches the stores through
  `AmbientSession<IAsyncDocumentSession>` / `AmbientSession<IDocumentSession>`, keyed by the database.

## Dependencies
- Birko.Data.Localization (`ILocalizedWriteBoundary`, `BestEffortWriteBoundary`), Birko.Data.RavenDB (stores,
  `IRavenConnectedStore`, `RavenAmbientKey`, `RavenDbUnitOfWork`), Birko.Data.Stores (`AmbientSession`, `StoreChain`),
  Birko.Data.Patterns (`TransactionRollbackOnlyException`)

## Maintenance
See the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
