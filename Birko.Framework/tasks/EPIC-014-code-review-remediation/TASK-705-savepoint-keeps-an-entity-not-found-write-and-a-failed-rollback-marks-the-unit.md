---
id: TASK-705
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-21
depends-on: [TASK-697]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A savepoint keeps what an entity-not-found write stored, and a failed rollback to it marks the caller's unit

## Context

Found 2026-10-21 by TASK-696's spec harvest of `entity-localization`, then measured. Two gaps in the savepoint path
[[TASK-694]] and [[TASK-697]] introduced:

1. `SqlSavepoint` rolled back on **any** exception, including `EntityNotFoundException`. A collection update that meets a
   missing entity applies the rest and then throws it (TASK-555), so inside a savepoint the applied part was undone.
   Measured live on PostgreSQL: a retried collection update in a caller's unit lost its stored entity. It reached every
   retried write on PostgreSQL (`SqlRetryableWrite`, TASK-694) and every declared-retried localized write (TASK-697).
2. When the rollback to the savepoint itself failed, the failure was only recorded on the exception
   (`SqlSavepoint.RollbackFailureKey`), and `SqlLocalizedWriteBoundary` left the caller's unit unmarked, so a half write
   could be committed.

Also a dead condition: `!unit.IsParticipant` in the boundary's own-transaction retry, which can no longer be false since
the async joined path stopped using a participant unit (TASK-697).

## Acceptance criteria

- [x] An `EntityNotFoundException` releases the savepoint and is rethrown, so what the write stored stays.
- [x] A failed rollback to the savepoint marks the caller's unit rollback-only in the boundary's declared-retry path.
- [x] The dead condition is removed.
- [x] Live tests on SQLite, PostgreSQL, SQL Server and MySQL, red with each fix removed.

## Out of scope

- MongoDB and RavenDB, which have no savepoints (their joined boundaries mark the unit on any failure).

## Human test plan

N/A — asserted by the live suites.

## Outcome

Done 2026-10-21. `SqlSavepoint` (the one producer) releases on `EntityNotFoundException`, so the rule holds for every
caller of a savepoint. `SqlLocalizedWriteBoundary.RunInSavepoint(Async)` marks the unit when the exception carries
`RollbackFailureKey`.

**Tests (live, four providers):**
- `TASK705_a_declared_retried_update_that_misses_an_entity_keeps_what_it_stored`: before, PostgreSQL lost the update;
- `TASK705_a_savepoint_that_cannot_be_rolled_back_marks_the_callers_unit`: the write ends the transaction under the
  savepoint (`ROLLBACK`), the rollback to it fails, and the unit is marked.

**Mutations, all red on the four providers:** the savepoint rolling back on `EntityNotFoundException` (4 red); a failed rollback not marking the unit (4 red).

**Regression:** Localization.SQL 144, SQL 723, Migrations.SQL 256, Patterns 161, Data 240.
