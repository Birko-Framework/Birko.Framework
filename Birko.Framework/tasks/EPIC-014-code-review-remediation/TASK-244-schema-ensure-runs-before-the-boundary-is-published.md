---
id: TASK-244
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-240, TASK-242, TASK-243, TASK-245]
findings: []
pr: 7680c56 (Birko.Data.Stores) · a8f7e6f (Birko.Data.SQL)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# Lazy schema-ensure runs before the store publishes its transaction boundary

## The ordering

Every SQL store's public CRUD wrapper is shaped:

```csharp
public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
{
    EnsureInitialized();          // <- schema-ensure happens HERE
    CreateCore(data, storeDelegate);   // <- the Core override publishes the boundary HERE
}
```

`EnterTransactionScope()` lives inside `*Core` (and, for the provider overrides, was only put there by
[[TASK-242]]). So a store's lazy `CREATE TABLE` **never** runs inside the boundary published from
`TransactionContext`, and — where the caller installed the ambient itself, e.g. through `SqlUnitOfWork` —
it **always** does, because the ambient travels with the flow rather than with the store.

Two different orderings for the same operation depending on which door the caller used. Neither is
obviously the intended one, and nothing states which it is.

## Why this is filed separately from TASK-243

[[TASK-243]] was the *measured harm*: on MySQL the DDL implicitly committed the open transaction, so the
boundary was silently lost.

**TASK-243 shipped without settling this**, and deliberately — it fixed *which connection* schema DDL runs
on (MySQL: its own; every other provider: the boundary's) and left *whether schema-ensure belongs inside a
caller's unit of work* exactly where it was. The `blocks` relation this file used to carry was therefore
wrong and has been dropped. What TASK-243 did settle, and what this task inherits:

- On **MySQL** a table created by schema-ensure inside a boundary now survives the rollback, because the
  DDL is no longer part of it. There is a test pinning that.
- On **PostgreSQL, SQL Server and SQLite** it is still rolled back with the boundary. There is a test
  pinning that too, deliberately asserting the opposite of the MySQL one.
- Neither loses data — the next operation re-runs schema-ensure — **but the store is left believing it is
  initialised**, and that is the residue this task owns (see the third question below).

So the two answers now differ by provider, on purpose, and this task is the question of whether they
should differ at all.

## Questions to settle

- **Should schema-ensure ever participate in a caller's transaction?** Argument for: a create-then-write
  inside one boundary is atomic. Argument against: schema is not the caller's unit of work, and a rollback
  that also un-creates the table makes the *next* request pay for it again.
- **If not, how does it stay off the boundary without opening a second connection?** On SQLite that is
  exactly the deadlock TASK-242 exists to remove. "Run it outside" cannot mean "run it on another
  connection" on every provider.
- **Is `EnsureInitialized` in the public wrapper even the right place** now that `*Core` owns the scope?
  Moving the scope up into the wrapper (before `EnsureInitialized`) is the one-line alternative and gives
  the opposite answer — worth costing, since it makes both doors agree.
- **What should a store believe after its schema-ensure was rolled back?** On the transactional-DDL
  providers the table is gone but `_initialized` is true, so the same store instance reused after a caught
  rollback skips schema-ensure and meets a missing table. On PostgreSQL that then hits the
  missing-relation swallow (TASK-211) and reads as an empty result. Narrow — it needs the same store
  instance reused across a rollback — but it is the concrete cost of schema-ensure participating, and it
  is an argument for the answer being "it should not".

## Acceptance

- One stated, documented answer for whether schema-ensure participates, applied identically to the
  ambient door and the `SetTransactionContext` door.
- A test per provider pinning the chosen ordering — including SQLite, where the wrong choice **deadlocks
  rather than misbehaves**. That is measured, not feared: TASK-243's revert R2 made the suppression
  unconditional and all three SQLite lazy-init tests failed with `SQLite Error 5: 'database is locked'`.
  `Birko.Data.SQL.SqLite.Tests.LazyInitInsideBoundaryEndToEndTests` already exists and will catch it.
- If the answer is "schema-ensure does not participate", the four existing rollback pins change meaning —
  update them rather than deleting them, since the pair of opposite assertions is the record of why the
  providers were allowed to differ.

---

## Consumer evidence — a live instance that left a database unbuildable (Symbio TASK-527, 2026-08-22)

This stopped being a design question with a hypothetical cost. Symbio hit an instance whose symptom is
exactly what this ordering predicts, and it cost a working session and a test environment.

**Reported symptom.** On a wiped SQLite database, `POST /api/auth/setup` returned **200** and logged
success, while the `Users` table **was never created** — not in `sqlite_master`, no row. Every table the
same operation wrote *after* the user (`Tenants`, `UserLogins`, `UserProfiles`, `UserTenants`, `Roles`,
`RolePermissions`, `UserRoles`) existed and was populated. Login then failed forever as a bodyless 401, and
a process restart did not recover it. Hand-creating **only** the `Users` table made the whole seed run.

**Why it points here.** The operation runs inside one `ITransactionBoundary.RunAsync`. `_users.CountAsync()`
is its first data access, so `Users` is the first store to schema-ensure. For SQLite to end up with the
later writes committed and that one `CREATE TABLE` absent, the DDL and the DML cannot have been on the same
connection — a single SQLite transaction cannot commit its DML and roll back its DDL. That is precisely the
split this task describes: `EnsureInitialized()` runs before `*Core` publishes the boundary.

⚠ **It does not reproduce on demand.** Re-measured the same day on the same commit: **four** from-scratch
bring-ups all succeeded, including the exact documented path (stop API → delete the file → start → seed).
So the trigger is conditional — ordering, process state, or which store happens to be touched first — and
the evidence above is a single observation, recorded because it is the only one anybody has. Treat it as a
lead, not as a specification.

**What the consumer built instead of a workaround**, so a recurrence arrives with evidence rather than as an
anecdote: `tools/verify-fresh-database.ps1` in the Symbio repo — delete → boot → setup → **log in** →
assert the table exists. It exists because every cheaper signal passes in the failing state: unit tests
never build a database from nothing through the host, integration checks run against an already-built one,
`/health/ready` opens a connection but reads no row, and `setup` itself answers **200**.

**Priority raised P3 → P1 on this evidence.** The original P3 reflected an ordering question with a
theoretical cost. The measured cost is a database that cannot be created and an auth surface that fails
closed with no diagnosable error.

---

## The answer (2026-08-23)

**Schema-ensure participates in the caller's boundary, and a participating schema-ensure is not
remembered.** Applied identically to both doors. The measurements that forced it, in the order they were
taken:

1. **Can schema-ensure DDL run on a connection other than the boundary's?** Yes — and it did, through the
   `SetTransactionContext` door, on every provider. `EnterTransactionScope()` lived only in `*Core`, so the
   boundary was not published while `EnsureInitialized()` ran; `DoDdlCommand` then found no ambient and took
   `RunCommandTransaction`, its own connection. **On SQLite it cannot even begin one** — measured
   `SQLite Error 5: 'database is locked'` after the command timeout, with the full stack
   `InitCoreAsync:137 → CreateTable → DoDdlCommand:279 → RunDdl:290 → DoCommandWithTransaction:236 →
   RunCommandTransaction:455 → BeginTransaction`. So one of the two doors could not work at all, which
   settles the "which ordering was intended" question without appeal to taste.
2. **But that is NOT the consumer's mechanism, and saying so is a result.** That path throws — a 500, not
   Symbio's 200. The tempting conclusion from the evidence ("the DDL ran on another connection") is
   measurably the wrong half.
3. **The consumer's symptom reproduces deterministically from the residue instead**
   (`SchemaEnsureRollbackResidueTests`): schema-ensure inside a boundary that rolls back → the table goes,
   `_initialized` stays true → the next write on the same store instance **returns a Guid, stores nothing,
   and the table still does not exist**. Every symptom Symbio reported follows, including "a restart did not
   recover it" being consistent with the seed re-running against a table that is never created.
4. **The half that makes it silent is a different defect**, now [[TASK-277]]:
   `SqLiteConnector.OnException` answers "no such table" by calling `DoInit()` and not rethrowing, and
   `DoInit` only raises `OnInit`, which nothing in the framework subscribes to. The residue alone loses one
   operation; the swallow is what answers 200.

### What changed

- `AbstractStore` / `AbstractAsyncStore`: `_initialized = CanRememberInitialization`, a `protected virtual`
  hook defaulting to `true`.
- `AbstractConnector.DdlSurvivesRollback` = `AmbientTransaction == null || !SupportsTransactionalDdl` — the
  provider switch asked from the other side, so the lifetime decision and the DDL routing cannot disagree.
- `InitCore` / `InitCoreAsync` enter the transaction scope, so both doors agree.

### Answers to the four questions this task asked

- *Should schema-ensure ever participate?* **Yes** — it must, on SQLite (TASK-243 R2: unconditional
  suppression is a hang, not a smaller win), and the alternative door was already broken.
- *How does it stay off the boundary without a second connection?* It does not need to; that framing
  assumed the answer was "no".
- *Is `EnsureInitialized` in the public wrapper the right place?* It can stay: entering the scope inside
  `InitCore` makes both doors agree without moving the wrapper, and without a concrete store overriding
  public CRUD (§ Conventions forbids that shape).
- *What should a store believe after a rolled-back schema-ensure?* **That it is not initialised.** This was
  the residue, and it was the actual defect.

### Cost, and the alternative that was rejected

One idempotent `CREATE TABLE IF NOT EXISTS` on the boundary's own connection, on the next operation after a
boundary-scoped init — so a consumer whose every operation runs inside a boundary pays it per operation.
Invalidate-on-rollback (register a callback with the boundary, clear the flag) has no steady-state cost and
was rejected: it is correct only if **every** path that ends a boundary without committing is caught, and a
missed path silently restores the defect. Reach for it with a measured cost, not on principle.

### Verification

**1,292 tests, 0 failed, 0 skipped** across nine suites with `BIRKO_REQUIRE_LIVE` set throughout (live
PostgreSQL 16.15, SQL Server 2022, MySQL 8.4.11, on-disk SQLite): `Birko.Data.SQL` 624 · SQLite 236 (+3) ·
PostgreSQL 88 (+3) · MySQL 90 (+3) · MSSql 96 (+3) · Migrations.SQL 49 · InMemory 69 · JSON 23 · XML 18 —
**12 new**. The three non-SQL suites are there because the changed hook lives in the backend-agnostic
`Birko.Data.Stores`.

**Three mutations, disjoint and provider-correct:**

| Mutation | SQLite | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|---|
| remember unconditionally (revert the hook) | 1 red | 1 red | **green** | 1 red |
| drop the scope from `InitCore` (the per-store door) | 1 red (**3s** — the lock timeout) | 1 red | **green** | 1 red |
| capability ignores the provider switch | green | green | **1 red** | green |

MySQL being green in the first two and the only failure in the third is the shape that proves the provider
switch is load-bearing rather than decorative.

### ⚠ Not reported as a clean sweep

`Birko.Data.Migrations.SQL.Tests` failed
`SchemaBuilderBoundaryLeakTests.Without_a_runner_transaction_nothing_is_published_either` **once in 16 runs**
with `ObjectDisposedException: 'SQLitePCL.sqlite3'`. Measured against the pre-fix code (**0 in 10**) and
again after (**0 in a further 10**), so it is not attributable to this change. That test takes a
**process-wide cached connector** via `DataBase.GetConnector`, which is [[TASK-270]]'s subject and
[[TASK-276]]'s standing hypothesis — recorded there with the test name and the exception, which is more than
that task had.

### Deliberately not done

- **No attempt to reproduce Symbio's own bring-up.** It had already failed to reproduce four times; the
  ordering was traced in source and the mechanism rebuilt instead.
- **The SQLite swallow is not fixed** — [[TASK-277]], with a test that asserts the defect so it cannot be
  believed fixed. It is the swallow family (TASK-211), not this ordering, and it is still live for every
  other way a table can be absent.
- **`IsMissingTableException` / the reader swallow untouched** — TASK-211 kept it deliberately for
  view-existence probing; changing it needs its own decision.
- **No change to `EnsureInitialized`'s position** in the public wrapper, and no move of `EnterTransactionScope`
  up into it: entering inside `InitCore` achieves the same agreement without a concrete store overriding
  public CRUD.
