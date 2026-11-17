---
id: TASK-938
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-17
depends-on: [TASK-305]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A locked SQLite database made a write wait three timeouts

## Context

Reported by the Symbio session (Symbio TASK-1101, gate 2, 2026-11-17): `POST /api/eshop/shipping-methods` failed with
`SqliteException` "SQLite Error 5: database is locked" after **90 432 ms**. The HTTP request, the job scheduler and the
outbox stalled with it. The path: `AsyncDataBaseStore.CreateCoreAsync` → `AbstractAsyncConnector.InsertAsync` →
`DoCommandWithTransactionAsync` → `ExecuteWithRetryAsync`.

Microsoft.Data.Sqlite loops on SQLITE_BUSY / SQLITE_LOCKED inside the command until the command timeout (30 s by
default, `SqLiteSettings.CommandTimeout`). TASK-305's retry policy (2 retries) treats busy as "applied nothing" and
retries it, which is safe (rule 66) but repeats the whole wait: 3 × 30 s. The retry was sized for errors that arrive at
once (a SQL Server deadlock victim), not for one that ends a wait. TASK-305's `BusyRetryTests` relied on exactly that:
they held the lock past the timeout and passed because the retry waited again.

Symbio's 90 s figure is one measurement; its 10 reproduction rounds did not repeat it, and who held the lock is still
unknown (Symbio's own >5 s lock-wait warning is meant to find it). The task is filed on the mechanism, which the red
test reproduces at small scale, not on the incident (rule 54).

## Decision

The user's, 2026-11-17, over keeping TASK-305 as is (and documenting the (MaxRetries + 1) × timeout worst case) or
sharing one timeout across all attempts: **one wait only**. A busy error that arrives after the full timeout is not
retried; one that arrives early is. This changes TASK-305's behaviour: a lock held past `CommandTimeout` now fails after
one timeout.

## Change

- `AbstractConnectorBase.WaitedOutLockTimeout(ex, elapsed)`, a public virtual beside `IsNotAppliedException` (base:
  never). `ExecuteWithRetry[Async]` times each attempt and does not retry when it answers yes.
- `SqLiteConnector` answers yes for a busy/locked error once the attempt took at least `SqLiteSettings.CommandTimeout`
  (30 s on plain settings). A timeout of 0 (wait forever) never counts as waited out, so an early busy error (a WAL
  snapshot conflict skips the busy handler) stays retryable.
- `BusyRetryTests`: the three converging tests now hold the lock for less than the timeout (the busy-wait gets it) and
  are renamed; the control keeps its past-the-timeout lock with `RetryPolicy.None`.
- Rule 66 (CLAUDE-conventions.md) gets the sub-rule; spec `store-crud-contract` and CLAUDE.md § Recent Updates updated.

## Evidence

- Red, with the hook in place and answering "never": a lock held past a 1 s timeout failed after **3454 ms** (sync) and
  **3480 ms** (async), the 3 × timeout Symbio saw; the four predicate cases failed too.
- Green: `BusyRetryTests` 11/11, the failing write ends in about one timeout.
- Regression: every test project importing `Birko.Data.SQL` or `Birko.Data.SQL.SqLite`, 35 projects, all green (the live-database
  suites skip without their hosts).

## Acceptance criteria

- [x] A write blocked past the timeout fails after one timeout, not (MaxRetries + 1).
- [x] A busy error that arrives before the timeout is still retried.
- [x] Nothing in the other providers changes (base answers "never").

## Human test plan

N/A — fully covered by automated tests (the timing test holds a real write lock from a second connection).
