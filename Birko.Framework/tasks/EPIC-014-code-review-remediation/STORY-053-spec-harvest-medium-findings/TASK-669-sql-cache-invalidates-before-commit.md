---
id: TASK-669
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] A write through the cached store inside a transaction invalidates the table again **after** the transaction
      commits (an after-commit callback the boundary's owner runs, or an equivalent), on the ambient `SqlUnitOfWork`
      path and on the explicit `SetTransactionContext` path
- [x] The path that cannot be hooked (a caller committing the `DbTransaction` itself) is documented, or refused
- [x] A test reproduces the window red on HEAD: inside a transaction, write; outside it, read (caches the old rows);
      commit; read again sees the new rows
- [x] Regression: every suite importing `Birko.Data.SQL` and `Birko.Data.SQL.Caching`

## Human test plan

N/A — fully covered by automated tests (a library contract with no UI surface).

## Outcome (2026-10-18)

- **Hook.** New `AmbientSqlTransaction.OnCommitted(transaction, key, callback)`, keyed by the `DbTransaction` itself
  (held weakly). An ambient entry would not do: the explicit-context path publishes a fresh entry per operation, and a
  participant can run in another async flow. A second registration with the same key is ignored.
- **Who runs it.** The boundary's owner runs the callbacks once after a successful commit: `SqlUnitOfWork.CommitAsync`,
  and the bulk stores' `AtomicallyAsync` / `Atomically` for their own transactions. Rollback, a failed commit and
  disposal drop them. A joined participant's commit runs nothing.
- **Failures (rule 25).** A throwing callback never fails the committed transaction. It is recorded in the connector's
  `SubscriberFailures`, channel `AfterCommit`, through the new `AbstractConnector.RecordSubscriberFailure`, which
  `RaiseDiagnostic` now shares (one recorder).
- **Store.** `CachedAsyncDataBaseBulkStore` registers its invalidation on `TransactionContext.Transaction`, or else the
  ambient boundary for its database. The key is per store instance, so many writes invalidate once.
- **Documented, not covered.** A `DbTransaction` committed by its caller without a `SqlUnitOfWork` has no owner to run
  the work, so that path keeps the window. There was no clean way to refuse it.
- **Tests.** `Task669AfterCommitTests`, 6 on SQLite, with the outside reader in another flow under
  `AmbientSqlTransaction.Suppress()`: ambient path, explicit-context path, rollback, once per boundary, a bulk write's
  own transaction, a failing callback recorded. Red on HEAD 5/6, with `OnCommitted` stubbed as a no-op; the rollback
  test is a pin.
- **Regression.** Live, all 34 importers of Birko.Data.SQL / Birko.Data.SQL.Caching green. Sandbox builds.
- **Docs and spec.** Spec `caching` (the gap scenario replaced by three), spec `unit-of-work-and-transactions` (a new
  requirement with 3 scenarios), and spec `bulk-filter-operations` (`Atomically`). `docs/caching.md` and both
  CLAUDE.md files are updated.
