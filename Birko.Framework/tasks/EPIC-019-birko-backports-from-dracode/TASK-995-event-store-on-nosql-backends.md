---
id: TASK-995
parent: EPIC-019
feature: null
status: in-progress
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

## Measured 2026-11-26 (before deciding)

A temporary probe through TASK-990's `IStoreFactory` (not committed), live servers in Docker:

1. **Sequential:** create an entity with a caller-set Guid, then create another with the same Guid. On **every** backend
   (InMemory, JSON, XML, SQLite, MongoDB, Elasticsearch, RavenDB, Cosmos DB) `CreateAsync` keeps the caller's Guid and the
   second create throws `Birko.Data.Exceptions.StoreUniqueViolationException` ("Create never overwrites"); one row, the
   first, remains. No backend upserts.
2. **Race:** two independent factories create the same Guid at the same instant, 20 rounds per backend.

   | Backend | one winner | both "won" | loser's exception |
   |---|---|---|---|
   | SQLite (one file), MongoDB, Elasticsearch, RavenDB, Cosmos DB | **20/20** | 0 | `StoreUniqueViolationException` |
   | JSON, XML (one folder, two factories = two processes) | 19/20 | **1** (a write silently lost) | `UnauthorizedAccessException` (file lock) |

   The database backends refuse a duplicate atomically on the server. The file stores are safe within one process only
   (each factory keeps its own in-memory copy of the file); InMemory is single-process by nature.

**What this means for the decisions:** option (a) works on every database backend with no backend-specific code: a
**deterministic Guid derived from (aggregate, version)** (a name-based UUID) makes a second writer's create fail natively,
and `StoreUniqueViolationException` translates to `EventStoreConcurrencyException`. `SqlEventStore` already reads only
through the generic store API (filtered reads ordered by version), so one generic event store over `IAsyncBulkStore` is
mostly its code without the SQL unique index. `Birko.Data.EventSourcing` already references `Birko.Data.Stores`.

## Decisions (the user)

1. **Duplicates: a deterministic event id from (aggregate, version)** (2026-11-26). A second writer's create of that id
   fails natively on every database backend (measured above).
2. **Backends: every database backend, plus InMemory for tests** (2026-11-26). JSON and XML are out: they lost a write
   across two processes. Their READMEs say so.
3. **Shape: one generic event store, with per-platform overrides, the way the stores work** (2026-11-27). One interface,
   one working default over any `IAsyncBulkStore`, and a backend gets its own project only when it needs native
   behaviour. That means `.SQL` now, and others only where a measurement shows the default fails.

4. **A batch on a backend without transactions: options 1 and 2** (2026-11-27). Where a backend has native atomicity,
   its override uses it: RavenDB session, Cosmos DB transactional batch, MongoDB transaction on a replica set. Elsewhere
   (MongoDB standalone, Elasticsearch, InMemory), the default deletes its own partial batch on a conflict, and the README
   documents that a crash partway through a batch can leave its first events stored.
5. **A SQL factory answers `SqlEventStore`** (2026-11-27). It keeps a batch in one transaction. No existing data
   constrains this: DraCode confirmed its SQLite databases are test-only (and its history is already unreadable after a
   reload, DraCode TASK-226), and the user said the history in its local `koboldlair.db` can be thrown away. DraCode
   TASK-228 needs no migration step.

6. **Cosmos DB gets its own event store, partitioned by aggregate** (2026-11-27). The user asked which design is better as
   a pattern, and chose the recommended one. It uses its own container (`EventStreams`, `/aggregateGuid`), not the
   generic store, so an append is one transactional batch and a stream's reads stay within its partition. A batch
   mixing aggregates is refused.

## Implementation plan (draft, for the user's review before building)

**Step 0: measure before writing code.** Two facts the design depends on, measured per backend through the factory:
- What a bulk `CreateAsync` with a duplicate in the middle leaves behind: nothing, a prefix, or everything except the
  duplicate. This decides how the default makes a batch all-or-nothing (open point A).
- Whether a read made right after a create sees the new row. Elasticsearch is expected to miss it until a refresh. If
  it does, `GetVersion` returns a stale value and the next append conflicts with itself.

**Step 0 measured (2026-11-27).** A temporary probe (not committed) ran through the factory, against live servers in Docker:

| Backend | read right after a create | bulk create [2, 3, duplicate of 1, 4] leaves |
|---|---|---|
| InMemory, SQLite, RavenDB | sees it | **nothing** (all-or-nothing) |
| MongoDB | sees it | **2, 3** (ordered insert, stops at the duplicate) |
| Cosmos DB | sees it | **2, 3, 4** (everything but the duplicate) |
| Elasticsearch | **does not see it** (0 rows, no top version) | **2, 3, 4** |

Every backend threw `StoreUniqueViolationException`. A descending, limit-1 read answers the top version wherever the row
is visible. What this changes in the plan:
- **The default appends one event at a time, in version order.** A bulk create leaves three different results on
  conflict, so only a sequential append tells the default exactly which of its own events to delete.
- **RavenDB's bulk create is already all-or-nothing**, so its override is a bulk append (decision 4, option 2).
- **Cosmos DB's bulk create is not transactional.** Its override needs a transactional batch, which works only within
  one partition, so its event container is partitioned by aggregate.
- **Elasticsearch needs an override for read-your-writes** (a refresh on append). Without it, the current version reads
  0 straight after an append.
- **MongoDB:** its override uses a transaction when the server is a replica set. The Docker server is standalone, so the
  default path runs there.

**Step 1: `Birko.Data.EventSourcing` (generic, no backend references).**
- `Models/EventRecord.cs`: an `AbstractModel` with `DomainEventModel`'s fields. Its `Guid` is
  `EventRecord.IdFor(aggregate, version)`, a name-based UUID (v5, RFC 9562) under a fixed Birko namespace. The event's
  own id is a separate field.
- `Stores/AbstractAsyncEventStore.cs`: implements `IAsyncEventStore`. The public methods are non-virtual (rule 12):
  - they validate (null event, empty batch, a version below 1);
  - they translate `StoreUniqueViolationException` into `EventStoreConcurrencyException`, naming the
    (aggregate, version);
  - they call the protected `*Core` seams: `AppendRangeCoreAsync`, `ReadCoreAsync(aggregate, from?, to?)`,
    `GetVersionCoreAsync` and `ReadAllFromCoreAsync`.
- `Stores/AsyncStoreEventStore.cs`: the default, over `IAsyncBulkStore<EventRecord>`.
  - Reads use a filter ordered by version.
  - `GetVersion` reads the highest version: ordered descending, limit 1.
  - A batch is appended in version order. On a conflict, the events this batch already wrote are deleted by their
    ids before the exception is thrown (see open point A).
- `Stores/EventStoreFactoryExtensions.cs`: `factory.GetEventStore(o => o.AddSql())`. The overrides are registered at
  the composition root, as the backends are (`p => p.AddSqLite()`). Neither side can reference the other, and nothing
  is held in process-wide state (rule 36). It returns a backend's registered override when there is one, otherwise the
  default over `factory.GetAsyncStore<EventRecord>()`. Application code never names a backend.

**Step 2: `Birko.Data.EventSourcing.SQL`, the first override.** `SqlEventStore<DB>` derives from
`AbstractAsyncEventStore`. Its behaviour is unchanged (out of scope): one transaction per batch, its own unique index,
`MAX(Version)` and the `__Events` table. `o.AddSql()` makes any SQL factory (`ISqlStoreFactory`) answer with it, over
the factory's connector (open point B).

**Step 3: other overrides, only where measured.** Each one is its own `Birko.Data.EventSourcing.<Backend>`, as `.SQL`
is.
- Elasticsearch is expected: it needs a refresh on append, if step 0 shows the stale read.
- Cosmos DB is possible: a transactional batch, partitioned by aggregate, would make a batch natively atomic (open
  point A).
- A backend for which the default passes the contract suite gets no project.

**Step 4: one contract suite for every implementation.** The suite is
`tests/Shared/EventSourcing/EventStoreContract.cs`, an abstract test class with a `NewStore()` factory. It covers:
- append and read back in order;
- reads up to and from a version, the current version (0 when empty), and all events from a time;
- a duplicate version refused with `EventStoreConcurrencyException`;
- a conflicting batch leaving nothing behind;
- DraCode's two-writers test: two independent factories (two clients) race on one aggregate for N rounds, and each
  version ends with exactly one event.

Where it runs:
- InMemory, SQLite and the default over SQLite, in CI;
- `SqlEventStore` on PostgreSQL, MySQL and SQL Server, live;
- MongoDB, Elasticsearch, RavenDB and Cosmos DB, live under their `BIRKO_*_HOST` variables.

`SqlEventStore`'s existing tests stay.

**Step 5: docs, then DraCode.** Write READMEs and CLAUDE.md files for both projects (JSON and XML marked
single-process) and a Recent Updates entry, then tell DraCode how to call it.

**Open points (the user decides before building):**
- **A. An all-or-nothing batch on a backend without transactions** (MongoDB standalone, Elasticsearch). The default
  deletes what it wrote when a later event in the batch conflicts. A process that crashes partway can still leave the
  first events of a batch stored. They are valid, contiguous versions, but they record part of one command.
  Options:
  - accept and document this;
  - use native atomicity where a backend has it (RavenDB session, Cosmos DB transactional batch, MongoDB on a replica
    set);
  - refuse a multi-event batch on backends that cannot be atomic.
- **B. Which store a SQL factory answers.** `SqlEventStore` keeps the `__Events` table, so DraCode's existing history
  stays readable. The default would start a new table, and that history would need migrating.

## Progress

- **2026-11-27, steps 1–2 done** (`7c2ce67c`). These now exist:
  - `AbstractAsyncEventStore`;
  - `AsyncStoreEventStore` with `EventRecord` (name-based v5 ids, checked against the RFC example);
  - `factory.GetEventStore(o => …)` with `EventStoreOverrides`;
  - `SqlEventStore` rebased, behaviour unchanged, with `o.AddSql()`;
  - `tests/Shared/EventSourcing/EventStoreContract.cs`, 9 tests.

  The contract passes on InMemory and on `SqlEventStore` over SQLite. `SqlEventStore`'s 26 existing tests still pass.
- **2026-11-27, the default store against the live NoSQL servers** (a temporary run, not committed):

  | Backend | contract (9) | what failed |
  |---|---|---|
  | MongoDB (standalone) | **9/9** | — |
  | Cosmos DB | **9/9** | — (the batch compensation keeps nothing) |
  | RavenDB | 8/9 | two writers: `Database … already exists!` when two clients create a new database at once, [[TASK-996]] |
  | Elasticsearch | 3/9 | 5 tests read their own write too early (expected, step 0); two writers: `resource_already_exists_exception` when two clients create a new index at once, [[TASK-997]] |

  TASK-996 and TASK-997 are defects in the stores and predate this task: any two app instances starting together on a
  fresh database or index hit them. Each is filed as its own task, and the contract on RavenDB and Elasticsearch waits
  on them.
- **2026-11-27, TASK-996 and TASK-997 fixed** (`3a5a0ce2`, `f243fd67`). Stores that start together on a new RavenDB
  database or Elasticsearch index now all start.
- **2026-11-27, the Elasticsearch event store** (`Birko.Data.EventSourcing.ElasticSearch`). `ElasticSearchEventStore` is
  the default store plus an index refresh after every append, including after a failed batch's compensation. It is
  registered with `o.AddElasticSearch()`, which matches `ElasticSearchStoreFactory`. The contract passes live, 10 of 10
  (9 contract tests plus the override choice). Without the override, 5 of the 9 failed.

  The new project is registered in the `.slnx`, the workspace, the Sandbox aggregator, the spec map, `CLAUDE-projects.md`,
  the README and `docs/dependencies.md`. Fixed along the way: `Birko.Data.EventSourcing.SQL` and its tests were missing
  from the workspace. A sweep now finds every project in both lists.
- **2026-11-27, the RavenDB event store** (`Birko.Data.EventSourcing.RavenDB`). `RavenDBEventStore` writes a batch with one
  bulk create, a single `SaveChanges`, so it is all or nothing even across a crash. The conflicting version is found
  afterwards with `AsyncStoreEventStore.FindConflictAsync`, a new shared helper. Registered with `o.AddRavenDB()`. The
  contract passes live, 10 of 10.
- **2026-11-27, Cosmos DB and MongoDB checked against decision 4.**
  - **MongoDB:** the store joins a session's transaction (`IAsyncTransactionalStore<T, IClientSessionHandle>`), on a
    replica set only. Correction: the existing `birko-live-mongo` (and CI's) is already a one-node replica set. The
    container started for testing with the user's OK was turned into a **standalone** server (`birko-live-mongo-standalone`,
    port 27018), so the path without transactions is tested too.
  - **Cosmos DB:** a transaction covers one logical partition, and the Cosmos stores partition every document on its own
    id (TASK-614, TASK-240), so no batch of events can be atomic through the store. The user asked which design is
    better as a pattern. Recommended: an own Cosmos event store with a container partitioned by aggregate. That gives an
    atomic batch per aggregate, and a single-partition read of a stream and of its current version, where the default
    store fans out across all partitions. A batch that mixes aggregates is refused. Awaiting the user's confirmation.
- **2026-11-27, the MongoDB event store** (`Birko.Data.EventSourcing.MongoDB`). `MongoDBEventStore` decides per append:
  - inside the caller's MongoDB transaction, it joins it;
  - on a server with transactions (asked once with `hello`), the batch is one transaction of its own;
  - on a standalone server, it is the default store.

  Measured while building:
  - two first transactions creating the collection implicitly collide at commit, so the collection is created before
    any transaction;
  - two transactions inserting one `_id` get a `TransientTransactionError` write conflict, not a duplicate key, so the
    transaction is retried (at most 10 times) and the retry meets the duplicate key;
  - the conflict lookup is refused inside the aborted transaction, so it runs after the rollback.

  The contract passes on the replica set and on the standalone server, plus the caller's-transaction test: 21 of 21,
  5 runs in a row. Registered with `o.AddMongoDB()`. `live-tests.yml` now runs the MongoDB and RavenDB event store
  projects.
- **Noted:** `RavenDBEventStoreContractTests` failed 1 of 10 once, in 8 runs, and the failing test was not captured. A
  15-run loop with full output is in progress.
- **2026-11-27, the Cosmos DB event store** (`Birko.Data.EventSourcing.CosmosDB`, decision 6).
  - `CosmosDBEventStore : AbstractAsyncEventStore`. Container `EventStreams` partitioned on `/aggregateGuid`; a
    document's id is its version.
  - An append is one transactional batch. The batch result names the version that conflicted with 409.
  - A batch mixing aggregates, or over 100 events, is refused.
  - `ReadAllFrom` filters on UTC ticks.
  - It uses the factory's client. Registered with `o.AddCosmosDB()`.

  Live: 13 of 13 (9 contract tests plus 4 Cosmos rules), also 3 runs with `AZURE_COSMOS_BYPASS_QUERY_PARSING=true` (CI's
  Linux query-plan path). `live-tests.yml` runs it.
- **RavenDB flake explained:** the 15-run loop failed 2 times, both "two writers" on its first `GetVersion`, with
  `IndexCreationException`: two clients' first identical query on a new database, and one auto-index request timed out
  after 15 s. It is a store defect, not this task's code, and is filed as [[TASK-998]].

## Acceptance criteria

- [x] Decisions above recorded, with the per-backend duplicate behaviour measured, not assumed. Decisions 1–6, step 0, and every backend measured.
- [x] MongoDB event store with the full `IAsyncEventStore` contract; a concurrent same-version append is refused with
      `EventStoreConcurrencyException` across two processes (or two clients), live under `BIRKO_MONGO_HOST`.
      `MongoDBEventStore`: 21 of 21 on a replica set and a standalone server, including two writers racing as two clients.
- [x] Every other backend that is in passes the same contract suite (shared with `SqlEventStore`'s tests); one that is
      out says so in its README. Live: SQL/SQLite, InMemory, MongoDB (replica set and standalone), RavenDB (45 of 45 after
      [[TASK-998]]), Cosmos DB, Elasticsearch. JSON and XML are refused by `GetEventStore`, and their exclusion is
      documented in Birko.Data.EventSourcing's README.
- [x] An event store obtainable from the configured `IStoreFactory` without naming the backend (if decided in).
      `factory.GetEventStore(o => o.AddSql().AddMongoDB()…)`.
- [ ] READMEs / CLAUDE.md, Recent Updates; DraCode told.

## Out of scope

- Changing `SqlEventStore`'s behaviour, beyond answering the factory hook.
- Snapshots and projections.

## Human test plan

- [ ] In DraCode on MongoDB: two writers change one specification at once; both land, on distinct versions, and the history
      reads back in order.
