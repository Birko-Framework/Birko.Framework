---
id: TASK-694
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-685]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Savepoint-scoped writes inside a caller's transaction, so PostgreSQL can retry after a refusal

## Context

Spawned from [[TASK-685]] at plan time, 2026-10-20 (decided by the user). Inside a caller's transaction, a statement
PostgreSQL refuses (a unique violation) aborts the whole transaction. Every later statement then fails with `25P02`, so
nothing can re-read and retry there. TASK-685 makes the decorators surface the typed `StoreUniqueViolationException`
instead of the raw error. TASK-680 and [[TASK-683]] (`LocalizedTranslationRows`, `SqlLocalizedWriteBoundary`) route
around it the same way, or re-run the whole write in a new transaction when they own it.

A savepoint around each write inside a joined or explicit transaction would keep PostgreSQL's transaction usable:
`SAVEPOINT`, then `ROLLBACK TO` on refusal. Under READ COMMITTED the re-check after the rollback sees the other writer's
commit, so the in-place retry would work. The syntax differs: SQL Server uses `SAVE TRANSACTION`, while SQLite and MySQL
use `SAVEPOINT`.

It does not help MySQL. Its transaction survives a refusal anyway, but the re-check reads the REPEATABLE READ snapshot
from before the other writer committed (measured in TASK-684).

Bigger than TASK-685, because it touches every SQL write path. It costs two extra round trips per write inside a
transaction. It also changes behaviour: a PostgreSQL caller could catch a violation and carry on in its transaction.
Rule 35 frames it as a provider limit routed around in one place.

## Acceptance criteria

- [ ] Decide the scope: PostgreSQL only, or every provider through a connector's savepoint syntax. Decide whether
      savepoints are always on inside a transaction or opt-in. Measure the round-trip cost before deciding (rule 10:
      priced on the steady state).
- [ ] One producer in Birko.Data.SQL: a write inside a joined or explicit transaction rolls back to its savepoint on a
      unique violation, before the violation is translated.
- [ ] Live tests on PostgreSQL: the slug / default decorators and the translation upsert retry in place inside a
      caller's `SqlUnitOfWork`. TASK-685's and TASK-683's per-provider knobs are updated, and the tests that pinned
      "typed refusal" are inverted (rule 56).

## Out of scope

- MySQL's stale snapshot: a savepoint does not change what a REPEATABLE READ re-check sees.

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Populated by `/tasks plan TASK-694` — leave empty until then._
