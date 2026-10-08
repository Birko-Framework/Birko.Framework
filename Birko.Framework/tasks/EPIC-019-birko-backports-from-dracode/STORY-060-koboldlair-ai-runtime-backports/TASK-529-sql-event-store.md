---
id: TASK-529
parent: STORY-060
feature: null
status: todo
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

- [ ] An `IAsyncEventStore` on the SQL store hierarchy, provider-agnostic (in `Birko.Data.EventSourcing` or a new `Birko.Data.EventSourcing.SQL`)
- [ ] Append-range is one transaction (rule 6)
- [ ] Unique (aggregate, version); a concurrent append at the same version fails with a typed concurrency exception
- [ ] Current version via an aggregate query, not a full read
- [ ] Tests on SQLite and the live suites: append / read / version, concurrent-append conflict, a failing batch leaves nothing behind

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
