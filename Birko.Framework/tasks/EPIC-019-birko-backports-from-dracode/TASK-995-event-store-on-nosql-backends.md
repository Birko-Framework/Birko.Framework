---
id: TASK-995
parent: EPIC-019
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-11-26
depends-on: [TASK-990]
blocks: []
related: [TASK-990, TASK-988]
findings: []
pr: null
github-issue: null
jira-key: null
---

# An event store on the NoSQL backends, from the unified store factory

## Context

Asked for by DraCode (session dracode-aa, 2026-11-26). Its user decided DraCode builds every store on [[TASK-990]]'s
`IStoreFactory`, so a DraCode project may run on MongoDB. DraCode's specification history uses
`Birko.Data.EventSourcing.SQL` (`SqlEventStore<DB>`), and there is no event store for MongoDB or any other NoSQL backend.
DraCode's slice that moves its event store onto the factory waits on this task.

**What DraCode needs:** an `IAsyncEventStore` (`Birko.Data.EventSourcing/Events/IEventStore.cs`) for whichever backend is
configured, MongoDB at least, with the SQL store's contract:
- append with per-aggregate versions (`AppendAsync`, `AppendRangeAsync`);
- **refuse a second writer of the same (aggregate, version)** with `EventStoreConcurrencyException`, across processes.
  DraCode's test "two writers changing one specification at once land on distinct versions" must hold;
- read an aggregate's history, up to / from a version, its current version, and all events from a time.

**Bonus, asked for:** get the event store from `IStoreFactory`, as `GetAsyncStore<T>()` works for models, so DraCode
names no provider in code.

How the SQL store keeps the contract (`SqlEventStore.cs`): a **unique index on (AggregateGuid, Version)** that it ensures
itself, a unique violation translated to `EventStoreConcurrencyException`, and `MAX(Version)` for the current version.

## Open decisions (settle with the user before building)

- **How each backend refuses a duplicate (aggregate, version).** This is the whole difficulty; a store that upserts makes
  the second writer silently overwrite the first.
  - (a) a deterministic document id derived from (aggregate, version), so a create of an existing id fails natively:
    MongoDB `_id`, Cosmos DB `id` (per partition), RavenDB optimistic concurrency, Elasticsearch `op_type=create`. It
    needs every backend's create to *fail* on an existing id, never upsert: to be measured per backend first.
  - (b) a backend unique index / key policy: MongoDB compound unique index, Cosmos DB unique-key policy (fixed at container
    creation); not available on Elasticsearch, RavenDB or the file stores.
  - Which backends are in: MongoDB is required; Cosmos DB, RavenDB, Elasticsearch, InMemory (tests), JSON / XML? A
    backend that cannot refuse across processes is either left out or documented as single-process only (InMemory, files).
- **Where it lives:** one generic event store over the factory's stores (needs a capability such as "create fails on an
  existing id"), or one `Birko.Data.EventSourcing.MongoDB` (etc.) per backend as `.SQL` is.
- **The factory hook:** an `IEventStoreFactory` capability (`factory is IEventStoreFactory`) or an extension that maps the
  configured backend to its event store; and whether the SQL providers answer it with `SqlEventStore`.

## Acceptance criteria

- [ ] Decisions above recorded, with the per-backend duplicate behaviour measured, not assumed.
- [ ] MongoDB event store with the full `IAsyncEventStore` contract; a concurrent same-version append is refused with
      `EventStoreConcurrencyException` across two processes (or two clients), live under `BIRKO_MONGO_HOST`.
- [ ] Every other backend that is in passes the same contract suite (shared with `SqlEventStore`'s tests); one that is
      out says so in its README.
- [ ] An event store obtainable from the configured `IStoreFactory` without naming the backend (if decided in).
- [ ] READMEs / CLAUDE.md, Recent Updates; DraCode told.

## Out of scope

- Changing `SqlEventStore`'s behaviour, beyond answering the factory hook.
- Snapshots and projections.

## Human test plan

- [ ] In DraCode on MongoDB: two writers change one specification at once; both land, on distinct versions, and the history
      reads back in order.
