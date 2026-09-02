---
id: TASK-292
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-09-02
depends-on: []
blocks: []
related: [TASK-243, TASK-244, TASK-277, TASK-286, TASK-288, TASK-290]
findings: []
pr: d03357f (Birko.Data.SQL) + 19decf6 (Birko.Data.SQL.SqLite.Tests) + 1636a7a (PostgreSQL.Tests) + 0c54ef7 (MySQL.Tests) + e0f1820 (MSSql.Tests)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# The per-store transaction door remembers a schema-ensure that was rolled back

## What was wrong

TASK-244's rule is *"schema-ensure participates in the caller's boundary, and a participating
schema-ensure is **not remembered**"*, and its own acceptance asked for **one answer applied identically
to the ambient door (`SqlUnitOfWork`) and the per-store door (`SetTransactionContext`)**. The first half
landed on both doors — `SchemaEnsureRollbackResidueTests` pins that the per-store door now puts the DDL
inside the boundary. The second half landed on one, and the reason is an evaluation **order**:

```csharp
// AbstractAsyncStore.EnsureInitializedAsync
await InitCoreAsync(ct);
_initialized = CanRememberInitialization;      // read Connector.DdlSurvivesRollback

// AsyncDataBaseStore.InitCoreAsync
using var _tx = EnterTransactionScope();       // publishes the PER-STORE context as an ambient...
await Task.Run(() => Connector.CreateTable(...));
}                                              // ...and disposes it HERE, before the line above runs
```

`DdlSurvivesRollback` is `AmbientTransaction == null || !SupportsTransactionalDdl`. With the per-store
door the scope belongs to `InitCoreAsync` and is gone by the time the base asks, so the expression
answered **`true`** about a create sitting in a caller's still-open transaction. With `SqlUnitOfWork` the
caller holds the ambient across the whole operation, so the same expression answered `false` and the
store correctly forgot — which is exactly the asymmetry TASK-244 set out to remove.

**The sync store is the worse half.** `SqlUnitOfWork.FromStore` takes an `AsyncDataBaseStore`, so
`SetTransactionContext` is the **only** transaction door a sync store has — the one that did not work.
There was no unaffected path there to compare against.

## Why it is a P1 rather than a tidy-up

It manufactures the exact signature [[TASK-290]] is chasing, **on a legitimate path, with no `DROP` and
no concurrency at all**: the connector has a recorded `CREATE TABLE`, the store's init gate has passed,
and the table is not there. Measured on SQLite before the fix:

| next operation | result |
|---|---|
| `CountAsync` | **`0`** returned, **1 anomalous schema escape recorded**, `SchemaGeneration` 0 → 1 |
| `CreateAsync` | **throws** `Exception: INSERT INTO "DoorRows" … [schema-ensure escape: … but this connector already created it — DoorRows created …]` |

That is TASK-285's silently wrong count and TASK-277's reported write, in the condition TASK-286's
annotation was built to name — produced by the framework itself.

⚠ **It is not SQLite-specific, and that was measured rather than argued.** The condition is
`AmbientTransaction != null && SupportsTransactionalDdl`. Reverting the fix against live servers:

| provider | new test | why |
|---|---|---|
| SQLite 3.x | **red** | transactional DDL |
| PostgreSQL 16 | **red** | transactional DDL |
| SQL Server 2022 | **red** | transactional DDL |
| MySQL 8.4 | **green** | DDL is not transactional, so `DoDdlCommand` suppresses the ambient and the table survives the rollback (TASK-243) — remembering is **correct** there |

## The fix

Capture durability **inside `InitCore`/`InitCoreAsync`**, while the scope that method entered is still
published, and have `CanRememberInitialization` read the captured value:

```csharp
_initDdlSurvivedRollback = Connector.DdlSurvivesRollback;   // at the end of InitCore*, in the scope
...
protected override bool CanRememberInitialization
    => Connector == null || _initDdlSurvivedRollback;
```

Applied to `AsyncDataBaseStore` and `DataBaseStore`. Three properties of the shape are deliberate:

- **One producer.** It still asks `Connector.DdlSurvivesRollback` — the same expression `DoDdlCommand`
  consults — so the provider switch cannot drift. Only *when* it is asked changed, never *what*.
- **Defaults to `true`.** A store that never enters a boundary — the overwhelming majority — behaves
  exactly as before and pays nothing. The steady-state control asserts that.
- **Not a blanket "never remember".** That was the tempting one-liner and it is caught: forcing the field
  false reds 4 tests including the steady-state and healing controls.

## What this does NOT explain

⚠ **It is not consumer Symbio's mechanism, and that is stated rather than implied.** Symbio reaches
transactions through `SqlTransactionBoundary` → Birko's `SqlUnitOfWork` — the ambient door, which was
never affected — and its own `TransactionBoundaryTests` explicitly rejects `SetTransactionContext` for a
singleton store. Its 12 annotated escapes came from GET routes with no boundary at all. [[TASK-290]]
stays open for that.

## Acceptance

- [x] Both doors give the same answer about whether a participating schema-ensure may be remembered.
- [x] Asserted on the async store **and** the sync store — they keep separate `_initialized` flags and
      separate gates, so a fix to one of the two is how half of this looks green.
- [x] Asserted per provider on live servers, including MySQL's **opposite** answer, so the capability
      cannot be "unified" from symmetry.
- [x] The steady state is unchanged: a store with no boundary anywhere still remembers its init and does
      not re-schema-ensure per operation.
- [x] Mutation-proven, disjointly.

## Measurements

Verified with `BIRKO_REQUIRE_LIVE` set throughout, against live **PostgreSQL 16**, **MySQL 8.4**,
**SQL Server 2022** and on-disk **SQLite**: **1,415 tests, 0 failed, 0 skipped** across nine suites —
SqLite 298 (287 → 298), PostgreSQL 93, MySQL 98, MSSql 108, `Birko.Data.SQL` 655, Migrations.SQL 53,
InMemory 69, JSON 23, XML 18.

**Mutations, disjoint:**

| mutation | red |
|---|---|
| both stores read `Connector.DdlSurvivesRollback` again | **3** — 2 async + 1 sync; every ambient-door and `SchemaEnsureRollbackResidueTests` assertion stayed green |
| async store only | **2** (the async pair) |
| sync store only | **1** (the sync one) |
| `_initDdlSurvivedRollback` forced permanently false — the blanket fix | **4**, including the steady-state and `VanishedTableHealingTests` controls |
| revert against live PostgreSQL | **1 of 8**, the new test |
| revert against live SQL Server | **1 of 8**, the new test |
| revert against live MySQL | **0 of 8** — the provider-correct signature |

## Out of scope

- Whether the same evaluation-order trap exists for `CanTrustRememberedInitialization` (TASK-288's
  counter): it does not — that one is read at *use* time, outside any of this method's scopes, and it
  compares a counter rather than asking about an ambient.
- The non-SQL stores' `CanRememberInitialization`: they default to `true` and have no caller-owned
  transaction to participate in, which is the documented reason for the default.
