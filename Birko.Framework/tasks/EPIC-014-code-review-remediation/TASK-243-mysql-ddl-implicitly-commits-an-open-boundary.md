---
id: TASK-243
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: [TASK-242]
blocks: []
related: [TASK-240, TASK-242, TASK-244, TASK-245]
findings: []
pr: "Birko.Data.SQL 46aa678 · Birko.Data.SQL.MySQL 3e0ab73 · Birko.Data.SQL.PostgreSQL e49676d · Birko.Data.SQL.MSSql d7534f1 · Birko.Data.TimescaleDB fe8a41a · Birko.Data.SQL.View 3671fe6 · Birko.Data.SQL.MSSql.View 8676ae0 · Birko.Data.SQL.PostgreSQL.View c9706b5 · Birko.Data.SQL.MySQL.Tests 75a1ce6 · Birko.Data.SQL.SqLite.Tests 2bfbe32 · Birko.Data.SQL.PostgreSQL.Tests e0c8ee4 · Birko.Data.SQL.MSSql.Tests feed73b"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL, Birko.Data.SQL.PostgreSQL, Birko.Data.SQL.MSSql, Birko.Data.SQL.View, Birko.Data.TimescaleDB]
---

# On MySQL, a store's first operation inside a boundary silently commits that boundary

## The defect — measured, not inferred

Found while proving [[TASK-242]] against MySQL 8. A store initialises lazily: `EnsureInitialized` /
`EnsureInitializedAsync` run in the **public** CRUD wrapper and issue `CREATE TABLE IF NOT EXISTS`, which
— after [[TASK-240]] — routes DDL onto the **ambient connection** when a boundary is open.

**MySQL implicitly commits an open transaction before and after every DDL statement.** So a store whose
*first ever* operation happens inside a boundary commits that boundary before its own write even runs, and
the caller's later rollback undoes nothing.

Measured on MySQL 8.4, with the TASK-242 connector fix fully in place:

```
FreshTable();                       // table exists on the server
var store = AsyncStore();           // store not yet initialised
await using var uow = SqlUnitOfWork.FromStore(store);
await uow.BeginAsync();
await store.CreateAsync(threeRows); // EnsureInitializedAsync -> CREATE TABLE -> implicit COMMIT
await uow.RollbackAsync();
// committed rows: 3   (expected 0)
```

Silent on the way in (the DDL succeeds) and silent on the way out (the rollback reports success), so the
only evidence is rows that should not exist. Not a test artefact: a host resolving scoped stores per
request gets a fresh store instance per request and `_initialized` lives on the store, so the **first**
request that touches an entity inside a boundary loses that boundary.

`CREATE TABLE IF NOT EXISTS` is emitted on **every** schema-ensure, not only when the table is missing, so
this fires on a fully-migrated database as readily as on a fresh one.

## The fix — a provider capability, consulted at one funnel

`AbstractConnectorBase.SupportsTransactionalDdl` (default `true`, **`false` on MySQL alone**), consulted
by `AbstractConnector.DoDdlCommand` / `AbstractAsyncConnector.DoDdlCommandAsync`. Where it is false the
statement runs with the ambient boundary suppressed — `AmbientSqlTransaction.Suppress()`, a fresh cell
with no head — so the DDL lands on a connection of its own and the caller's transaction is untouched.

**Why the switch is safe, and why it must be a switch.** The two halves of the trade land on opposite
providers:

| | DDL on the boundary's connection | DDL on its own connection |
|---|---|---|
| SQLite | **required** — the only thing that works | blocks on the RESERVED lock the boundary holds, fails on the busy timeout |
| MySQL | commits the boundary, silently | **required** — and safe, MySQL permits the second connection |
| PostgreSQL / MSSql | works (transactional DDL) | would also work |

The MDL objection was measured away rather than mitigated: on MySQL 8.4 an open transaction holding a row
lock on a table does **not** block a concurrent `CREATE TABLE IF NOT EXISTS` on that same table (17 ms,
with the holder confirmed `RUNNING` with `trx_rows_locked = 1`). So issuing the DDL off the boundary is
not a metadata-lock hazard, and the second connection this implies costs nothing.

### Sixteen emitters behind one funnel, and the four overrides that first defeated it

`DoDdlCommand` replaced `DoCommandWithTransaction` / `DoCommand` in every schema emitter: `CreateTable`,
`CreateIndexes`, `DropIndexes`, `DropTable`, both `ALTER`s, the base view DDL, and the provider view DDL in
`Birko.Data.SQL.MSSql.View` / `.PostgreSQL.View`. The **existence probes** (`ViewExists`,
`MaterializedViewExists`, `SelectViewCount`) are reads and were deliberately left alone.

`inOwnTransaction` preserves what each emitter already did — the base `CreateTable` wraps, the provider
overrides of it do not — because this change is about *which connection* DDL runs on, not about giving it
atomicity it never had.

**The first attempt fixed the base and the tests still failed, 5 of 7.** `MySQLConnector`,
`PostgreSQLConnector`, `MSSqlConnector` and `TimescaleDBConnector` each **override**
`CreateTable(string, IEnumerable<string>)` with their own `DoCommand` call, so schema-ensure never reached
the funnel on any of them. Reverting just the MySQL override back to `DoCommand` reproduces the failure
exactly (7 of 38), which is what pins it.

`ExternalConnection` / `ExternalTransaction` are deliberately **not** suppressed: their only user is the
migrations `SqlSchemaBuilder`, which exists to run DDL in a transaction it owns.

## Proof

| Suite | Tests | Server |
|---|---|---|
| `Birko.Data.SQL.MySQL.Tests.LazyInitInsideBoundaryLiveTests` | 7 | MySQL 8.4 — the defect |
| `Birko.Data.SQL.PostgreSQL.Tests.LazyInitInsideBoundaryLiveTests` | 7 | PostgreSQL 16 — contract pin |
| `Birko.Data.SQL.MSSql.Tests.LazyInitInsideBoundaryLiveTests` | 7 | SQL Server 2022 — contract pin |
| `Birko.Data.SQL.SqLite.Tests.LazyInitInsideBoundaryEndToEndTests` | 3 | on-disk SQLite — the no-deadlock proof |

Every test starts from a **cold** store; that is the whole point, and it is why the sibling
`BulkTransactionBoundaryLiveTests` could not see this.

**The MySQL warm-up is gone.** TASK-242's suite carried `WarmUpAsync` in three tests purely because of
this defect. It was removed and all 38 MySQL tests pass cold — the strongest available evidence that the
warm-up was hiding a bug rather than isolating a variable, and TASK-243's own acceptance criterion.

### Reverts — every one measured

| Revert | Effect | Result |
|---|---|---|
| R1: `MySQLConnector.SupportsTransactionalDdl` back to `true` | MySQL rejoins the boundary for DDL | **7 of 38** MySQL fail (5 lazy-init + 2 now-cold bulk) |
| R2: base `SupportsTransactionalDdl => false` | suppression made unconditional | **3 of 3** SQLite fail — `SQLite Error 5: 'database is locked'` |
| R3: `MySQLConnector.CreateTable` back to `DoCommand` | the provider override bypasses the funnel | **7 of 38** MySQL fail |

R2 is the one worth keeping: it shows the blanket fix the task file originally listed as "the clean
answer" turns a silent wrong answer into a hang, which in a consumer reads as an outage.

### Full green

19 SQL-touching suites, 1,129 tests, all three servers live: SqLite 220, PostgreSQL 55, MySQL 38,
MSSql 44, Data.SQL 514, Views 59, View.Migrations 14, ViewModel 18, the four `*.View.Tests` (9/22/7/19),
Caching 7, Providers 8, Migrations.SQL 34, Sync.Sql 7, BackgroundJobs.SQL 25, Workflow.SQL 12,
EventBus.Outbox.SQL 17. Whole-solution build: 0 errors, no new warnings.

## Deliberate behaviour, stated rather than left implicit

- **On MySQL a table created by schema-ensure inside a boundary now SURVIVES the rollback**, because the
  DDL is no longer part of it. That is the intended outcome — a rollback that also un-created the table
  would make the next request pay for it again — and it has its own test so nobody mistakes it for a leak.
- **On PostgreSQL, SQL Server and SQLite it is still rolled back with the boundary**, and that has its own
  test too. The two answers differ because the providers differ; pinning both is what stops someone
  unifying them later by reasoning from symmetry. Neither loses data — the next operation re-runs
  schema-ensure — but the store is left believing it is initialised, which is the sharp edge [[TASK-244]]
  owns.

## Spawned

- **[[TASK-245]]** — MySQL cannot create **any** declared index. The base `CreateIndexSql` emits
  `CREATE INDEX IF NOT EXISTS`, which MySQL 8.4 rejects as a syntax error, and MySQL is the one provider
  that does not override it. Measured end-to-end: a `[CompositeIndex]` entity yields
  `IndexCreationFailure` and no index on the table. Silent by design since TASK-204 (schema-ensure records
  rather than throws), so it has been shipping unnoticed.

## What generalises

- **A funnel with four overrides is not a funnel.** The base emitters were rewired first and the fix
  measured as *not working*; the provider `CreateTable` overrides were the reason. Third instance of this
  shape in a fortnight (TASK-215's base wrappers, TASK-242's store `*Core` overrides, this). When
  introducing a funnel, grep for `override` on every method that reaches it before believing the wiring.
- **The "clean" provider-independent answer was the wrong one, and only a live SQLite run said so.** The
  task file proposed running schema-ensure outside any boundary. Measured (revert R2), that is a
  `database is locked` failure on SQLite. The right shape was a stated provider capability, because the
  requirement genuinely differs by provider.
- **Measure the objection before mitigating it.** The MDL hazard would have justified a much larger fix
  (existence-check before DDL, per-provider index probing). One `docker exec` proved it does not occur.
- **A warm-up in a test is a claim that needs an owner.** TASK-242 added one with a comment naming this
  task; closing this task removed it and the suite went green cold. A warm-up whose reason is not written
  down is indistinguishable from a bug being hidden.
