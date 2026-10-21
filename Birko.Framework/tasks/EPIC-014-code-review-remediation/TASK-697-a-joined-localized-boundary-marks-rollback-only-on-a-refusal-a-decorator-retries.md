---
id: TASK-697
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: [TASK-694]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A joined localized write boundary marks the caller's unit rollback-only on a refusal a decorator above it retries

## Context

Found by [[TASK-694]]'s close review (2026-10-20), not measured. A slug or default decorator can sit above a localized
wrapper that has a `SqlLocalizedWriteBoundary`, inside a caller's `SqlUnitOfWork`:

1. The entity write is refused by the slug's unique key.
2. `SqlLocalizedWriteBoundary.RunJoined` (Birko.Data.Localization.SQL/SqlLocalizedWriteBoundary.cs ~197-212) calls
   `MarkRollbackOnly()` and rethrows.
3. The decorator above catches the refusal and retries successfully.
4. The caller's `CommitAsync` then fails, because the unit is rollback-only, although every write succeeded.

This already happened on SQL Server, which retries in place. TASK-694 makes PostgreSQL retry in place too, so it now
reaches PostgreSQL.

## Acceptance criteria

- [x] Measure it live (PostgreSQL and SQL Server): slug decorator → localized wrapper with a boundary → SQL store,
      inside a caller's unit, with a racing writer. Record whether the commit fails (rule 54).
- [x] If it does, decide how the boundary tells "a refusal someone above will retry" from "a write that failed". For
      example, a refusal that ran inside a retryable write's savepoint does not mark the unit. Then fix it, with the live
      test inverted.

## Out of scope

- The upsert's own retry (TASK-694).

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

1. Measure: slug decorator → racer → localized wrapper with `SqlLocalizedWriteBoundary` → SQL store, inside a caller's
   `SqlUnitOfWork`, another writer taking the slug after the decorator's probe.
2. `RetryableWrite` declares the retry for the flow (`IsDeclared`); a joined boundary then runs the write in a savepoint
   (`SqlSavepoint`, the one producer, now also `SqlRetryableWrite`'s) and does not mark the unit.

## Outcome

Done 2026-10-21.

**Measured first (rule 54):** on PostgreSQL and SQL Server the slug decorator retried and every write succeeded, but the
caller's `CommitAsync` threw `TransactionRollbackOnlyException`. MySQL's refusal stands (its snapshot hides the other
writer), and SQLite has no racer.

**Why not just stop marking:** the mark exists because a failed joined write can leave half of itself in the caller's
transaction (the entity written, its translations refused). So a write a caller declared retried runs in a savepoint:
a failure is rolled back to it, and nothing is left to poison. On PostgreSQL it nests inside the retrying store's own
savepoint. The async joined path now goes through the boundary itself rather than a participant `SqlUnitOfWork`, so
both paths decide alike.

**Tests (live, SQLite / PostgreSQL / SQL Server / MySQL):**
- `TASK697_a_refusal_the_slug_decorator_retries_does_not_poison_the_callers_unit`: the reproduction, now green
  (MySQL asserts its typed refusal);
- `TASK697_a_declared_retried_write_that_fails_is_undone_and_leaves_the_callers_unit_committable`, and its sync twin:
  a declared-retried write whose translation write fails after the entity write leaves nothing in the committed unit;
- the existing `A_callers_unit_of_work_is_joined_and_a_failed_write_poisons_it` still holds for an undeclared write.

**Mutations, all red:** sync / async declared branch removed (4 / 6 red); declared branch without the savepoint (3 / 3
red: PostgreSQL survives, its retrying store's savepoint covers it); `IsDeclared` always false (10 red); the async joined
path left on the participant unit (6 red).

**Not covered, stated:** MongoDB and RavenDB have no savepoints, so their joined boundaries (TASK-700) still mark the
unit on any failure; a decorator's retry there ends in a refused commit. That is the safe direction (never half a
write), and it is documented in the Localization README.
