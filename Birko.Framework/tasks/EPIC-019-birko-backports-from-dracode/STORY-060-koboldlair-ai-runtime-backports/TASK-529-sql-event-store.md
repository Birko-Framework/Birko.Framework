---
id: TASK-529
parent: STORY-060
feature: null
status: verify
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Ship a SQL IAsyncEventStore

## Context

`Birko.Data.EventSourcing` defines `IAsyncEventStore`, and nothing implements it apart from `EventStoreEventBus` and test
doubles, so every consumer writes its own. DraCode wrote `Data/Repositories/Sql/SqlEventStoreRepository.cs` (141) +
`Data/Entities/DomainEventEntity.cs` (72) on `AsyncSqLiteModelRepository`. Do not copy its defects:

- `AppendRangeAsync` is documented atomic but issues one `CreateAsync` per event with no transaction
- no unique (aggregate, version) constraint, so no optimistic concurrency — a process-local `SemaphoreSlim` is the only guard
- `GetVersionAsync` reads every row of the aggregate and takes `Max` in memory (`AggregateAsync` exists)
- SQLite only

DraCode `_loose/TASK-080` (rename `AggregateId` → `AggregateGuid`) touches that entity and goes away if Birko owns it.

Adopted in the consumer by DraCode TASK-127 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] An `IAsyncEventStore` on the SQL store hierarchy, provider-agnostic (in `Birko.Data.EventSourcing` or a new `Birko.Data.EventSourcing.SQL`)
- [x] Append-range is one transaction (rule 6)
- [x] Unique (aggregate, version); a concurrent append at the same version fails with a typed concurrency exception
- [x] Current version via an aggregate query, not a full read
- [ ] Tests on SQLite and the live suites — **SQLite done; live suites written, not yet run** (Docker was not running): append / read / version, concurrent-append conflict, a failing batch leaves nothing behind

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan

Done directly.

## Resolution (2026-10-10) — code done; live run pending

New project **`Birko.Data.EventSourcing.SQL`** (registered in the `.slnx`, CLAUDE-projects.md catalog, dependency
flow and tests list): `Models/DomainEventModel.cs` (`__Events`, `[CompositeIndex(UNIQUE AggregateGuid, Version)]`,
`OccurredAt` `[UtcField]` + indexed, `EventGuid` a column of its own — the store assigns the row `Guid`) and
`SqlEventStore<DB> : IAsyncEventStore`. In `Birko.Data.EventSourcing`: `EventStoreConcurrencyException`
(`AggregateGuid`, `Version`) — contract-level, so any backend throws the same type. In `Birko.Data.SQL`:
`AbstractConnectorBase.IsUniqueViolationException`, overridden by code on all four providers (SQLite 19 + 2067/1555,
SQL Server 2601/2627, PostgreSQL 23505, MySQL 1062; walks `InnerException`).

- **Unique (aggregate, version) is ensured explicitly** on first use via `CreateIndexes` (throws if unbuildable)
  rather than trusted to lazy schema-ensure, which only records a failure (rule 49 / TASK-204).
- **Append-range** runs in `SqlUnitOfWork` (joins a caller's boundary). A unique violation → rollback →
  `EventStoreConcurrencyException` naming the taken (aggregate, version) — looked up after the rollback, falling
  back to the batch when the lookup itself fails (PostgreSQL refuses statements in an aborted joined transaction).
- **Version** = one `AggregateAsync` `MAX(Version)`.
- ⚠ **Measured on the way:** with the unit of work removed, every SQLite test still passed — the provider stores'
  bulk insert is atomic by itself (SQLite/MySQL own a transaction, PostgreSQL one `COPY`, SQL Server one
  `SqlBulkCopy` batch). But the `SqlSettings` constructor builds the **generic** `AsyncDataBaseBulkStore`, whose
  bulk create inserts row by row. A test through the generic store now fails without the unit of work.

**Proof.** `tests/Birko.Data.EventSourcing.SQL.Tests/SqlEventStoreTests.cs` (11, SQLite file DB, stable over 3
runs): round trip of every field, version slices / time tail, `MAX` version, typed conflict writes nothing, a batch
conflicting part-way leaves nothing (provider store and generic store), duplicate inside a batch, **8 concurrent
writers racing read-version-then-append end with unique, gap-free versions and at least one conflict**, an append
inside a caller's unit of work rolls back / commits with it, the unique index exists in `sqlite_master`, and the
SQLite classifier (unique yes, wrapped yes, missing table / NOT NULL no). **Mutations:** index not unique → 5 red;
classifier answering false → 5 red; unit of work removed → 1 red (the generic-store test).

**Live:** `UniqueViolationClassifierLiveTests` added to the PostgreSQL, MSSql and MySQL test projects (duplicate →
true, wrapped → true, NOT NULL → false). They compile and skip without `BIRKO_*_HOST`; **not run** — Docker Desktop
was not running. Run them per `.github/workflows/live-tests.yml` before closing.
