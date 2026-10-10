# Birko.Data.EventSourcing.SQL

## Overview

SQL-backed `IAsyncEventStore` from Birko.Data.EventSourcing, on any SQL provider (SQLite, PostgreSQL, SQL Server,
MySQL). Built on `AsyncDataBaseBulkStore<DB, DomainEventModel>`, and shaped like `Birko.EventBus.Outbox.SQL` /
`Birko.BackgroundJobs.SQL` (a core contract needs a row). TASK-529 — before it, nothing implemented the contract and
every consumer wrote its own (DraCode's `SqlEventStoreRepository`, SQLite-only, is what this replaces).

## Structure

```
Birko.Data.EventSourcing.SQL/
├── Models/
│   └── DomainEventModel.cs   - one row per event in "__Events"; UNIQUE (AggregateGuid, Version); FromEvent/ToEvent
└── SqlEventStore.cs          - SqlEventStore<DB> : IAsyncEventStore
```

## Dependencies

- Birko.Data.EventSourcing (IAsyncEventStore, IEvent, DomainEvent, EventStoreConcurrencyException)
- Birko.Data.SQL (AsyncDataBaseBulkStore, AbstractConnector.IsUniqueViolationException, SqlUnitOfWork, attributes)
- Birko.Data.Stores (OrderBy, AggregateQuery)

## Key Design Decisions

**The unique index is the concurrency guard, so it is created explicitly.** `(AggregateGuid, Version)` is
`UNIQUE`; two writers appending the same version — in any number of processes — cannot both commit, and the loser
gets `EventStoreConcurrencyException` (aggregate + version). Lazy schema-ensure only *records* an index it could
not build (TASK-204), so the store calls `CreateIndexes` itself on first use, which throws instead (rule 49). A
process-local lock, DraCode's only guard, protects nothing once a second instance runs.

**An append-range is one transaction (rule 6).** It runs in a `SqlUnitOfWork`, which joins a caller's enclosing
boundary on the same database — so an entity write and its events commit or roll back together, and a conflict
marks the caller's boundary rollback-only. ⚠ The provider stores' (`AsyncSQLiteStore`, …) bulk insert is atomic on
its own; the **generic** `AsyncDataBaseBulkStore` — what the `SqlSettings` constructor builds — inserts one row at
a time, and only the unit of work makes its batch atomic. A test pins that path (it fails without the unit of work).

**Conflicts are recognised by error code, per provider** — `AbstractConnectorBase.IsUniqueViolationException`:
SQLite 19 / 2067 / 1555, SQL Server 2601 / 2627, PostgreSQL 23505, MySQL 1062.

**The current version is one `MAX(Version)` query** (`AggregateAsync`), not a read of every event.

**`EventGuid` is a column of its own.** The store assigns the row's `Guid` on create, so the event's id cannot ride
on it.

## Gotchas

- Table `__Events`, created on first use; no DDL needed. Index names `UX___Events_AggregateGuid_Version` and
  `IX___Events_OccurredAt` are database-global.
- SQLite settings are `PasswordSettings`, not `SqlSettings` — pass a configured `AsyncSQLiteStore<DomainEventModel>`
  to the store constructor there.
- `ReadAllFromAsync(from)` is `OccurredAt >= from`, ordered by time, then aggregate and version.
- The live classifier tests (`UniqueViolationClassifierLiveTests` in the PostgreSQL / MSSql / MySQL test projects)
  need the Docker servers from `live-tests.yml`.
