---
id: TASK-240
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-214, TASK-237]
findings: []
pr: "Birko.Data.Patterns e4f9c71 · Birko.Data.SQL 0fab171 · Birko.Data.MongoDB 032ed3d · Birko.Data.CosmosDB 3d383fb · Birko.Data.RavenDB c005820 · Birko.Data.ElasticSearch 079f9c0 · Birko.Data.InfluxDB 8a7d6c6 · Birko.Data.SQL.Tests de8d561 · Birko.Data.SQL.SqLite.Tests 704c047 · Birko.Data.SQL.PostgreSQL.Tests b0d2afc · Birko.Data.MongoDB.Tests ba916a6 · Birko.Data.RavenDB.Tests 2e49295 · Birko.Data.CosmosDB.Tests 479fefd · Birko.Data.ElasticSearch.Tests f569aa9"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.Patterns, Birko.Data.MongoDB, Birko.Data.RavenDB, Birko.Data.CosmosDB, Birko.Data.ElasticSearch, Birko.Data.InfluxDB]
---

# A transaction boundary that async writes actually honour, stated per provider

## Context

Filed from the consumer side as Symbio's TASK-442. Symbio has ~121 service methods across 24 modules
performing two or more awaited repository writes with nothing wrapping them (measured 2026-08-17,
syntactically — an order of magnitude, not a defect count). `PaymentService.ChargeAsync` (6 writes),
`OrderService.CreateAsync` (4) and `AuthService.SetupAsync` (12) are the shape. Every intermediate state
is visible to a concurrent HTTP reader and a mid-way failure leaves the operation half-applied.

**The consumer cannot fix this — the boundary is not expressible in the framework.**

### Note on the brief's stated next id

The brief said the next free task id was TASK-239. It is not: `TASK-239-over-declared-packages-net10-provides.md`
already exists. This is TASK-240.

## The defect — measured, not inferred

All three facts in the brief were re-verified against the source on 2026-08-17 and **all three hold
exactly, line numbers included**.

1. **The sync connector honours an external transaction.**
   `Birko.Data.SQL/SQL/Connectors/AbstractConnector.cs` — `ExternalConnection`/`ExternalTransaction`
   (:190-191), `SetExternalTransaction` (:197), and both `DoCommand` (:168) and
   `DoCommandWithTransaction` (:205) branch on them into `RunCommandWithExternalTransaction`.
   `RunReaderCommand` (:347) branches too, so sync reads inside the boundary see uncommitted writes.

2. **The async connector inherits those properties and ignores them.**
   `AbstractAsyncConnector : AbstractConnector` (:17). `DoCommandAsync` (:34),
   `DoCommandWithTransactionAsync` (:55), `RunCommandTransactionAsync` (:74), `RunCommandAsync` (:106)
   and `RunReaderCommandAsync` (:134) contain **no such branch**. Each does
   `await using var db = CreateConnection(_settings)` and the transactional one opens its own
   `BeginTransactionAsync` per statement batch.

3. **The consumer-facing hook exists and silently does nothing.**
   `Birko.Data.SQL/Stores/AsyncDataBaseStore.cs:43-47` — `SetTransactionContext` calls
   `Connector?.SetExternalTransaction(...)`, which the async write path never reads. A caller sets a
   transaction, gets no error, and every write commits outside it.

### What the brief did not know, and it changes the answer

- **`isLock: true` has ZERO call sites framework-wide.** Grepped every `.cs` in the framework: the only
  occurrence of the token outside the parameter declaration and the `if (!isLock)` tests is a comment.
  So `_asyncLock` (AbstractAsyncConnector.cs:22) is currently **unreachable**. The brief's concern #2
  (deadlock between a boundary holder and the gate) cannot be triggered today — but the fix must still
  not create it, which it does not, because the ambient branch is checked **before** the gate is taken.

- **A full `IUnitOfWork<TContext>` family already exists for six backends** —
  `Birko.Data.Patterns/UnitOfWork/IUnitOfWork.cs` plus `SqlUnitOfWork`, `MongoDbUnitOfWork`,
  `RavenDbUnitOfWork`, `CosmosDbUnitOfWork`, `ElasticSearchUnitOfWork`, `InfluxDbUnitOfWork`. The brief's
  survey does not mention it. `SqlUnitOfWork` already produces a correct `SqlTransactionContext`; the
  only missing link is that nothing carries that context into the async command helpers.

- **Mongo and Cosmos honour the context on WRITES ONLY — every read bypasses it.** The brief's survey
  marks both "✅ honoured in write paths", which is literally true and reads as "done". It is not:
  `AsyncMongoDBStore.ReadCoreAsync` (:93, :248) and `CountCoreAsync` (:131) call
  `Collection.Find(...)` / `CountDocumentsAsync(...)` with **no session argument**, so inside a Mongo
  transaction you cannot read your own uncommitted writes. `AsyncCosmosDBStore` is the same (:207, :312
  go to `GetItemLinqQueryable` directly). **RavenDB appeared to be the only backend that honours it on
  both** — `AsyncRavenDBStore` :186 (`LoadAsync`) and :214 (`Query<T>()`). ⚠ It routes reads through the
  session but does not actually deliver read-your-own-writes; probing a live server disproved this, see
  Results and [[TASK-241]].

  This matters because read-then-write is the shape of every one of the consumer's motivating methods.
  A boundary whose reads escape it gives a stale snapshot, which is a *wrong answer* rather than a
  missing feature.

- **ElasticSearch and InfluxDB have a UnitOfWork but no store hook at all.** Neither store implements
  `IAsyncTransactionalStore`, so their UoW's context can only be driven by hand.

- **`SetExternalTransaction` has exactly four callers**, all verified: three in
  `Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs` (:116, :225, :319 — startup schema work,
  single-threaded) and the store hook itself. No consumer calls it; Symbio calls neither it nor
  `SetTransactionContext`. Confirmed across both repos.

## The trap, restated

Connectors are cached process-wide per `(connector type, settings id)` in `DataBase.GetConnector` (:56)
and `GetAsyncConnector` (:68). `SetExternalTransaction` mutates state on that shared singleton. Copying
it to the async path would make one request's transaction capture every concurrent request's writes.
The existing sync feature is therefore **not a model to copy** — it is safe only because its sole real
caller runs at startup on one thread.

## Decision — what this task defines

**A cross-backend capability contract, plus a per-backend boundary carrier.** Stated plainly: the
*contract* is general; the *mechanism* is per-provider, because the backends' units of work genuinely
differ and a contract that hid that difference would be the same defect this task exists to remove.

### 1. `ITransactionCapabilities` on `IUnitOfWork` — the honest per-provider answer

Modelled on `IJobLockProvider.IsLeaseBased`: surface the distinction rather than smooth it over.
Every `IUnitOfWork` declares `Capabilities`, answering four questions a caller actually needs:

| provider | atomicity | scope | reads see own writes | server requirement |
|---|---|---|---|---|
| SQL | `Atomic` | `Database` | ✅ | none |
| MongoDB | `Atomic` | `Cluster` | ✅ *(this task)* | **replica set** |
| RavenDB | `Atomic` | `Cluster` | ❌ *(planned ✅ — **corrected after measuring**, see Results)* | cluster-wide tx |
| CosmosDB | `Atomic` | `SinglePartition` | ❌ *(batch is write-only by design)* | none |
| ElasticSearch | `BestEffort` | `None` | ❌ | none |
| InfluxDB | `BestEffort` | `None` | ❌ | none |

### 2. SQL mechanism: an ambient `AsyncLocal` scope, not store or connector instance state

Three options were considered.

- **Explicit parameter threaded through the command helpers** — rejected. It changes every store method
  signature and the `*Core` override convention on every backend, and pushes the threading into the
  consumer's 121 call sites, where forgetting one is silent.
- **Per-scope connector instance** — rejected. It requires changing `DataBase.GetConnector`'s caching
  *and* the consumer's DI wiring (Symbio registers `AddSingleton<IAsyncBulkStore<T>>`), and it still
  does nothing for a singleton store.
- **Ambient `AsyncLocal` scope** — chosen. It travels with the async control flow, so it is correct
  under concurrent request threads *and* under a singleton store over a shared connector — which is
  precisely the trap. It needs no consumer DI change, and it composes across stores automatically:
  a boundary spanning three entity types does not require the caller to remember three
  `SetTransactionContext` calls.

**Why this is not "what Mongo/Raven/Cosmos already do", as the brief asks.** Those three keep the
context as **instance state on the store**, which is safe only while the store is per-scope. Symbio
registers SQL stores as singletons over a process-wide cached connector, so instance state there is
exactly as unsafe as connector state. The ambient scope is the same idea with the lifetime problem
removed — and it is *additionally* used to implement the store-instance door, so both doors share one
mechanism rather than disagreeing about what "in a transaction" means (§ CLAUDE.md "one producer").

The ambient entry is **keyed by settings id**, so a scope opened against database A cannot capture a
write to database B, and nested scopes against different databases compose.

### 3. `SetTransactionContext` stops failing silently

On the SQL stores it no longer mutates the shared connector. It becomes per-store instance state that
the store's own operations enter as an ambient scope for their duration — matching Mongo/Raven/Cosmos
exactly, with the same documented caveat (safe while the store is per-scope). ElasticSearch and InfluxDB
**cannot accept a context at all** — they do not implement `IAsyncTransactionalStore` — which is the
honest "no", pinned by a test that asserts the interface is absent (the § SH-H006 rule: "assert the
off-interface property with a test — 'I didn't add it' is construction, not evidence").

### 4. Nesting joins rather than double-committing

`SqlUnitOfWork.BeginAsync` inside an existing ambient scope for the same settings id **joins** it as a
participant: no second connection, no second transaction, and commit/rollback are no-ops on the
transaction itself. A participant's `RollbackAsync` marks the scope **rollback-only** so the owner's
commit throws. This is the brief's concern #1 taken seriously — a participant that committed would
produce a committed inner transaction inside an outer one that later rolls back, i.e. partial
application reporting green.

## Acceptance criteria

- [x] Two awaited writes in one boundary, the second failing -> **nothing** committed, asserted by
      reading the rows back. Proven on **both** SQLite and PostgreSQL 16.
- [x] **Concurrency**: a caller inside a boundary and a caller outside it, in parallel against the same
      cached connector, do not capture each other's writes. Two-way overlap on PostgreSQL; on SQLite the
      handshake is one-way because SQLite serialises writers at the file level (measured -- the outside
      write sat on the writer lock for the full timeout).
- [x] Reads inside a SQL boundary see the boundary's own uncommitted writes.
- [x] The no-boundary path is unchanged -- 14 suites green, and Symbio 1972/1972.
- [x] Per provider, each backend proves it honours or refuses a boundary: Mongo against a **replica set**
      (with a test that asserts the topology first, so nothing below it can be a vacuous pass), Cosmos
      showing the single-partition limit is **enforced** at the call site, ElasticSearch showing it
      **refuses structurally**.
- [x] Mongo reads honour the session, so read-your-own-writes holds inside a Mongo transaction.
- [x] Every guard proven able to fail -- four reverts, splits below.
- [x] Symbio still builds and its suite still passes.
- [x] Decision recorded in `Birko.Data.SQL/CLAUDE.md` and in each NoSQL provider's own `CLAUDE.md`.

## What shipped

**Decision: a cross-backend capability contract + a per-backend boundary carrier.** The contract is
general; the mechanism is per-provider, because the backends' units of work genuinely differ.

- `Birko.Data.Patterns` -- `ITransactionCapabilities` / `TransactionCapabilities`, plus
  `IUnitOfWork.Capabilities`. Four questions a caller needs: atomicity, scope, whether reads see the
  boundary's own writes, whether a server topology is required. Modelled on `IJobLockProvider.IsLeaseBased`.
  Also `TransactionRollbackOnlyException`.
- `Birko.Data.SQL` -- `AmbientSqlTransaction` (an `AsyncLocal` cell, keyed by settings id); the ambient
  branch in both connectors' entry points and both readers; `SqlUnitOfWork` enrols and supports joining;
  `SetTransactionContext` on all four stores stopped mutating the shared connector and is now honoured
  through the same ambient mechanism.
- `Birko.Data.MongoDB` -- `FindIn` / `CountIn`, so every read path runs in the session.
- `Birko.Data.CosmosDB` -- `RequireBatchPartition` + `CosmosTransactionScopeException` on the whole verb
  family.
- Capability declarations on all six units of work.

### The per-provider answer

| provider | atomicity | scope | reads see own writes | server requirement |
|---|---|---|---|---|
| SQL | Atomic | Database | yes | none |
| MongoDB | Atomic | Cluster | yes *(fixed here)* | **replica set** |
| RavenDB | Atomic | Cluster | **no** *(corrected -- see below)* | none |
| CosmosDB | Atomic | **SinglePartition** (= one entity here) | no, by construction | none |
| ElasticSearch | BestEffort | None | no | none |
| InfluxDB | BestEffort | None | no | none |

## Results

**Green:** 14 suites, 1,360 tests, with live PostgreSQL 16, MongoDB 7 (replica set) and RavenDB 7.2.
Symbio: build succeeded, **1972/1972**.

New tests: `AmbientSqlTransactionTests` 14 (offline), `TransactionBoundaryEndToEndTests` 13 (real SQLite),
`TransactionBoundaryLiveTests` 10 (live PostgreSQL), `MongoTransactionBoundaryLiveTests` 6 (live replica
set), `RavenTransactionBoundaryLiveTests` 5 (live), `CosmosTransactionBoundaryTests` 7 (offline by design),
`ElasticSearchTransactionRefusalTests` 7 (offline by design).

**Mutation tests (revert -> red -> restore by reversing the exact substitution -> green):**

| revert | what it undoes | split |
|---|---|---|
| A | ambient unwired from `AbstractAsyncConnector` (the filed defect) | **7 of 10** PostgreSQL, **9 of 207** SQLite |
| B | ambient made process-wide instead of flow-local (**the naive fix**) | **3 of 10** PostgreSQL, **1 of 207** SQLite |
| C | Mongo session unwired from the read helpers | **2 of 97** |
| D | Cosmos partition guard disabled | **3 of 61** |

**Revert B is the one that matters.** The three PostgreSQL failures are *exactly* the three concurrency
tests; every single-threaded test still passed. That is what the brief predicted a naive implementation
would look like from a suite that does not run two flows at once.

## Corrections to the brief, and things found on the way

- **The stated next task id was stale** -- TASK-239 already existed. This is TASK-240.
- **`isLock: true` has zero call sites framework-wide**, so `_asyncLock` is currently unreachable and the
  predicted deadlock cannot be triggered today. The ambient branch is still placed before the gate.
- **A full `IUnitOfWork<TContext>` family already existed for six backends** and the brief's survey did not
  mention it. `SqlUnitOfWork` already produced a correct `SqlTransactionContext`; only the carrier was
  missing.
- **The survey's "honoured in write paths" was writes-only for Mongo and Cosmos.** Literally true, and it
  reads as done. Mongo's read paths took no session at all -- fixed here. Cosmos cannot be fixed: a
  `TransactionalBatch` exposes no read.
- **Cosmos's boundary is narrower than "single partition" suggests.** The store partitions by entity
  `Guid`, so every document is its own logical partition and a two-entity boundary is impossible by
  construction, not merely limited.
- **RavenDB's capability had to be corrected downward, and it exposed a separate P1 defect.** Probing
  read-your-own-writes against RavenDB 7.2 showed a session query never sees unsaved documents, and that
  `ReadAsync(Guid)` returns null for a document that exists. Root cause: `StoreAsync(data)` lets Raven
  auto-generate the document id while every read/update/delete addresses `guid.ToString()`. Measured
  consequences outside any transaction: **`DeleteAsync(entity)` is a silent no-op** and
  **`UpdateAsync(entity)` inserts a duplicate**. Filed as [[TASK-241]], not fixed here.

## Lessons worth carrying

- **An `async` method cannot publish an `AsyncLocal` to its caller.** `AsyncMethodBuilder.Start` saves the
  ambient `ExecutionContext` and restores it when the state machine returns -- including for writes made
  before the first `await`. So `await uow.BeginAsync()` could not install the scope, and the first
  implementation silently did nothing. The fix is a mutable cell installed from a **synchronous
  constructor**. There is now a test pinning the runtime fact itself, so the design comment cannot quietly
  stop being true.
- **Correctness must not depend on the scope being restored**, for the same reason: `DisposeAsync` cannot
  restore it either. A boundary is therefore marked `IsEnded` and skipped by lookup, so a flow holding a
  stale cell cannot resolve a disposed connection.
- **A test can be wrong about the design rather than about the code.** The first isolation test asserted
  that another store in the *same flow* escapes the boundary. It should not -- covering every store in one
  flow is the entire point. The assertion moved to a different flow, which is what the trap is actually
  about.
- **SQLite could not express half the proof, and that is why the brief demanded both engines.** Not merely
  "weaker evidence": a concurrent reader under an open write transaction blocks for the full busy timeout,
  so the scenario cannot run at all. Recorded in the SQLite suite where the test would have been, rather
  than dropped.
- **A skipped live test must not read as a pass.** The family's suites gate with a bare
  `if (host is null) return;`. New live suites print a SKIPPED line and fail outright under
  `BIRKO_REQUIRE_LIVE`, and the Mongo suite additionally proves the server is a replica set before
  asserting anything that needs one.

## Notes

- A skipped live test must not read as a pass. The suites here gate with a bare
  `if (host is null) return;`, which is indistinguishable from success. New live tests emit a
  visible skip marker and the run reports which backends were actually exercised.
</content>
</invoke>
