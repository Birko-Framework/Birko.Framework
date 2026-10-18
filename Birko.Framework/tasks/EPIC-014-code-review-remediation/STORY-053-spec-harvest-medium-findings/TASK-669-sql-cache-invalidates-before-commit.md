---
id: TASK-669
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: []
findings: [SH-M044]
pr: null
github-issue: null
jira-key: null
---

# The SQL cached store invalidates before a transaction commits, so a concurrent reader can cache pre-commit rows

## Context

Spawned by [[TASK-169]], which fixed the other half of SH-M044: a read inside a transaction (the store's own
`TransactionContext` or an ambient `SqlUnitOfWork`) no longer consults or fills the cache, so uncommitted rows are never
cached for other callers.

What remains: `CachedAsyncDataBaseBulkStore` invalidates the table's cached reads right after its write, which inside a
transaction is **before the commit**. A reader outside the transaction that misses in that window reads the committed
(pre-write) rows and caches them, and that entry survives the commit for the whole `DefaultExpiration`. A rollback is
harmless (the cached rows are the committed ones).

There is no commit hook to invalidate from. `SqlUnitOfWork.CommitAsync` has none; a store given an explicit context
publishes a fresh ambient entry per operation (`EnterTransactionScope`), so a callback registered there would be lost
when the operation ends; and a consumer that commits the `DbTransaction` itself bypasses `SqlUnitOfWork` entirely.

## Acceptance criteria

- [ ] A write through the cached store inside a transaction invalidates the table again **after** the transaction
      commits (an after-commit callback the boundary's owner runs, or an equivalent), on the ambient `SqlUnitOfWork`
      path and on the explicit `SetTransactionContext` path
- [ ] The path that cannot be hooked (a caller committing the `DbTransaction` itself) is documented, or refused
- [ ] A test reproduces the window red on HEAD: inside a transaction, write; outside it, read (caches the old rows);
      commit; read again sees the new rows
- [ ] Regression: every suite importing `Birko.Data.SQL` and `Birko.Data.SQL.Caching`

## Human test plan

N/A — fully covered by automated tests (a library contract with no UI surface).
