---
id: TASK-243
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-240, TASK-242, TASK-244]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL]
---

# On MySQL, a store's first operation inside a boundary silently commits that boundary

## The defect — measured, not inferred

Found while proving [[TASK-242]] against MySQL 8. A store initialises lazily: `EnsureInitialized` /
`EnsureInitializedAsync` run in the **public** CRUD wrapper and issue `CREATE TABLE` through the connector,
which — after TASK-240 — routes DDL onto the **ambient connection** when a boundary is open.

**MySQL implicitly commits an open transaction on any DDL statement.** So a store whose *first ever*
operation happens inside a boundary commits that boundary before its own write even runs, and the caller's
later rollback undoes nothing.

Measured on MySQL 8, with the TASK-242 fix in place:

```
FreshTable();                       // table exists on the server
var store = AsyncStore();           // store not yet initialised
await using var uow = SqlUnitOfWork.FromStore(store);
await uow.BeginAsync();
await store.CreateAsync(threeRows); // EnsureInitializedAsync -> CREATE TABLE -> implicit COMMIT
await uow.RollbackAsync();
// committed rows: 3   (expected 0)
```

Adding a warm-up read before the boundary makes it 0. That warm-up is what
`Birko.Data.SQL.MySQL.Tests.BulkTransactionBoundaryLiveTests.WarmUpAsync` does, and it documents this task
as the reason.

**Provider-specific.** PostgreSQL and SQL Server have transactional DDL and showed no such behaviour in the
same suites; SQLite likewise. MySQL (and MariaDB) are the exposure.

## Why it matters beyond the test

This is not a test artefact. A consumer using scoped stores per request — which is the shape Symbio ships —
resolves a **fresh store instance** per request, and `_initialized` lives on the store. The *first* request
that touches an entity inside a transaction boundary silently loses that boundary. It is silent on the way
in (DDL succeeds) and silent on the way out (rollback reports success), so the only evidence is rows that
should not exist.

## Shape of a fix (not decided)

Three candidates, in rough order of preference:

1. **Run schema-ensure outside any ambient boundary.** The clean answer, and it is provider-independent —
   see [[TASK-244]], which is the same ordering problem stated without MySQL. Schema-ensure is not part of
   the caller's unit of work and has no business inside it. Needs care: it must not open a second
   connection while SQLite's boundary holds the write lock, so "outside the boundary" cannot mean "on
   another connection" on every provider.
2. **Refuse to lazily create inside a boundary** — throw naming the explicit door (`InitAsync()` /
   `CreateTable`) so a host initialises up front. Loud and correct, but it turns a working-by-accident
   first request into a failure on the providers where the DDL is transactional and harmless today, so the
   blast radius needs measuring first (§ SH-H037: fail-fast is legitimate only where an opt-out exists and
   is checked first — here `InitAsync()` is that opt-out).
3. **Document it.** Weakest: the failure is silent, so a document nobody reads changes nothing.

## Acceptance

- A gated MySQL test that fails without the fix: store not warmed up, boundary opened, single-row **and**
  bulk write, rollback, **0 committed rows**.
- The same test on PostgreSQL and SQL Server, to pin that whatever is done does not regress the providers
  where this already works.
- SQLite: prove the chosen mechanism does not deadlock against a held write lock.
- State whether `Birko.Data.SQL.MySQL.Tests`'s `WarmUpAsync` can be removed afterwards, and remove it if so
  — a warm-up that outlives its reason is a test hiding a bug.
