---
id: TASK-244
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-240, TASK-242, TASK-243, TASK-245]
findings: []
pr: ""
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
